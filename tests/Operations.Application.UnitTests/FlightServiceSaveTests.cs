using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightServiceSaveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] InitialVersion = [1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_with_an_open_employee_period_returns_field_errors_before_any_write_or_upload(bool existing)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        if (existing) (await fixture.SaveAsync(id)).IsSuccess.ShouldBeTrue();
        var writes = fixture.Writes.SaveCalls;
        var input = fixture.Input with
        {
            EndUtc = Now.AddMinutes(30), Attachments = [NewImage()],
            Performers = [fixture.ClosedPerformerInput.Performers.Single(),
                new(Guid.NewGuid(), fixture.Other.StaffMemberId, Now)]
        };

        var result = await fixture.SaveAsync(id, input);

        result.Error.Code.ShouldBe("Operations.FlightService.OpenPerformerPeriods");
        result.Error.Failures!.Keys.ShouldContain("EndUtc");
        result.Error.Failures.Keys.ShouldContain("Performers[1].EndUtc");
        result.Error.Failures.Keys.ShouldNotContain("Performers[0].EndUtc");
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.SaveCalls.ShouldBe(0);
        var records = (await fixture.ReadAsync()).Services;
        records.Count.ShouldBe(existing ? 1 : 0);
        if (existing) records.Single().Window.ToUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Another_users_end_locks_selection_and_delete_without_locking_creator_core_corrections()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var input = fixture.ClosedPerformerInput;
        (await fixture.SaveAsync(id, input)).IsSuccess.ShouldBeTrue();
        input = input with { EndUtc = Now.AddMinutes(30) };
        (await fixture.SaveAsync(id, input, fixture.Other)).IsSuccess.ShouldBeTrue();
        var correction = await fixture.SaveAsync(id, input with { Notes = "Creator's correction preserves contributors" });
        correction.IsSuccess.ShouldBeTrue();
        correction.Value.Service.CanEdit.ShouldBeTrue();
        correction.Value.Service.CanChangeService.ShouldBeFalse();
        correction.Value.Service.CanDelete.ShouldBeFalse();
        var nextServiceId = Guid.NewGuid();
        fixture.Reader.AllowedServicesOverride = new HashSet<Guid> { input.ServiceId, nextServiceId };
        var writes = fixture.Writes.SaveCalls;

        var change = await fixture.SaveAsync(id, input with { ServiceId = nextServiceId });
        var deletion = await fixture.DeleteAsync(id);

        change.Error.Code.ShouldBe("Operations.FlightService.SelectionReadOnly");
        deletion.Error.Code.ShouldBe("Operations.FlightService.DeleteReadOnly");
        fixture.Writes.SaveCalls.ShouldBe(writes);
        var retained = (await fixture.ReadAsync()).Services.ShouldHaveSingleItem();
        retained.Service.ServiceId.ShouldBe(input.ServiceId);
        retained.Notes.ShouldBe("Creator's correction preserves contributors");
        retained.HasOtherUserEnd.ShouldBeTrue();
        retained.UpdatedByUserId.ShouldBe(fixture.Owner.UserId); // Last-editor audit is not contribution ownership.
    }

    [Fact]
    public async Task Own_only_contributions_allow_selection_changes_and_delete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var input = fixture.ClosedPerformerInput with { EndUtc = Now.AddMinutes(30), Attachments = [NewImage()] };
        var created = await fixture.SaveAsync(id, input);
        created.IsSuccess.ShouldBeTrue();
        created.Value.Service.CanChangeService.ShouldBeTrue();
        created.Value.Service.CanDelete.ShouldBeTrue();
        var nextServiceId = Guid.NewGuid();
        fixture.Reader.AllowedServicesOverride = new HashSet<Guid> { input.ServiceId, nextServiceId };

        var changed = await fixture.SaveAsync(id, input with { ServiceId = nextServiceId, Attachments = null });

        changed.IsSuccess.ShouldBeTrue();
        changed.Value.Service.ServiceId.ShouldBe(nextServiceId);
        changed.Value.Service.CanDelete.ShouldBeTrue();
        (await fixture.DeleteAsync(id)).IsSuccess.ShouldBeTrue();
        fixture.Storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Exact_retry_acknowledges_legacy_closed_work_with_open_staff_before_applying_the_new_rule()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        (await fixture.SaveAsync(id)).IsSuccess.ShouldBeTrue();
        var flight = await fixture.ReadAsync();
        var service = flight.Services.Single();
        var closed = TimeWindow.Create(fixture.Input.StartUtc, Now.AddMinutes(30)).Value;
        typeof(FlightService).GetProperty(nameof(FlightService.Window))!.SetValue(service, closed);
        typeof(FlightService).GetProperty(nameof(FlightService.EndedByUserId))!.SetValue(service, fixture.Owner.UserId);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        await fixture.ChangeVersionAsync();
        var input = fixture.Input with { EndUtc = closed.ToUtc };
        var writes = fixture.Writes.SaveCalls;

        var acknowledged = await fixture.SaveAsync(id, input, version: InitialVersion);
        var changed = await fixture.SaveAsync(id, input with { Notes = "A new invalid correction" });

        acknowledged.IsSuccess.ShouldBeTrue();
        acknowledged.Value.Service.Performers.Single().EndUtc.ShouldBeNull();
        changed.Error.Code.ShouldBe("Operations.FlightService.OpenPerformerPeriods");
        fixture.Writes.SaveCalls.ShouldBe(writes);
    }

    [Fact]
    public async Task Legacy_end_without_known_attribution_keeps_existing_delete_protection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = Now.AddMinutes(30) }))
            .IsSuccess.ShouldBeTrue();
        var flight = await fixture.ReadAsync();
        var record = flight.Services.Single();
        typeof(FlightService).GetProperty(nameof(FlightService.EndedByUserId))!.SetValue(record, null);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        FlightServiceDto.Map(record, fixture.Owner.UserId, true).CanDelete.ShouldBeFalse();
        (await fixture.DeleteAsync(id)).Error.Code.ShouldBe("Operations.FlightService.DeleteReadOnly");
        (await fixture.ReadAsync()).Services.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task Save_preserves_client_ids_and_distinct_ids_allow_repeated_catalog_services()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var first = await fixture.SaveAsync(firstId);
        var second = await fixture.SaveAsync(secondId, fixture.Input with
        {
            Performers = [fixture.Input.Performers.Single() with { Id = Guid.NewGuid() }]
        });

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        first.Value.Service.Id.ShouldBe(firstId);
        second.Value.Service.Id.ShouldBe(secondId);
        first.Value.Service.Performers.ShouldHaveSingleItem().Id.ShouldBe(fixture.Input.Performers.Single().Id);
        var flight = await fixture.ReadAsync();
        flight.Services.Count.ShouldBe(2);
        flight.Services.ShouldAllBe(service => service.Service.ServiceId == fixture.Input.ServiceId);
    }

    [Fact]
    public async Task Noncreator_may_set_the_end_of_open_work_without_changing_its_creator_or_core()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput)).IsSuccess.ShouldBeTrue();
        var end = Now.AddMinutes(30);

        var result = await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = end }, fixture.Other);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Service.CreatedByUserId.ShouldBe(fixture.Owner.UserId);
        result.Value.Service.EndedByUserId.ShouldBe(fixture.Other.UserId);
        result.Value.Service.EndUtc.ShouldBe(end);
        result.Value.Service.Notes.ShouldBe(fixture.ClosedPerformerInput.Notes);
        result.Value.Service.CanEdit.ShouldBeFalse();
        result.Value.Service.CanSetEnd.ShouldBeTrue();
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("performer")]
    [InlineData("media")]
    public async Task Noncreator_cannot_mix_a_core_performer_or_media_change_with_closing_work(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput)).IsSuccess.ShouldBeTrue();
        var input = fixture.ClosedPerformerInput with { EndUtc = Now.AddMinutes(30) };
        input = change switch
        {
            "notes" => input with { Notes = "Unauthorized correction" },
            "performer" => input with { Performers = [input.Performers.Single() with { StartUtc = Now }] },
            _ => input with { Attachments = [NewImage()] }
        };
        var writes = fixture.Writes.SaveCalls;

        var result = await fixture.SaveAsync(id, input, fixture.Other);

        result.IsFailure.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.Files.ShouldBeEmpty();
        var service = (await fixture.ReadAsync()).Services.ShouldHaveSingleItem();
        service.Window.ToUtc.ShouldBeNull();
        service.Notes.ShouldBe(fixture.ClosedPerformerInput.Notes);
        service.Performers.ShouldHaveSingleItem().Window.FromUtc.ShouldBe(fixture.ClosedPerformerInput.Performers.Single().StartUtc);
        service.Attachments.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creator_cannot_change_or_clear_another_users_recorded_end(bool clear)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var end = Now.AddMinutes(30);
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput)).IsSuccess.ShouldBeTrue();
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = end }, fixture.Other)).IsSuccess.ShouldBeTrue();
        var writes = fixture.Writes.SaveCalls;

        var result = await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = clear ? null : end.AddMinutes(5) });

        result.IsFailure.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(writes);
        var service = (await fixture.ReadAsync()).Services.ShouldHaveSingleItem();
        service.Window.ToUtc.ShouldBe(end);
        service.EndedByUserId.ShouldBe(fixture.Other.UserId);
    }

    [Fact]
    public async Task Creator_can_correct_core_fields_while_preserving_another_users_end()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var end = Now.AddMinutes(30);
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput)).IsSuccess.ShouldBeTrue();
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = end }, fixture.Other)).IsSuccess.ShouldBeTrue();

        var result = await fixture.SaveAsync(id, fixture.ClosedPerformerInput with
        {
            EndUtc = end, Notes = "Corrected service notes", CustomerReferenceNumber = "REF-CORRECTED"
        });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Service.Notes.ShouldBe("Corrected service notes");
        result.Value.Service.CustomerReferenceNumber.ShouldBe("REF-CORRECTED");
        result.Value.Service.EndUtc.ShouldBe(end);
        result.Value.Service.EndedByUserId.ShouldBe(fixture.Other.UserId);
        result.Value.Service.CreatedByUserId.ShouldBe(fixture.Owner.UserId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recorded_closer_can_correct_or_clear_the_end_without_owning_the_core(bool clear)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var end = Now.AddMinutes(30);
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput)).IsSuccess.ShouldBeTrue();
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = end }, fixture.Other)).IsSuccess.ShouldBeTrue();
        DateTimeOffset? corrected = clear ? null : end.AddMinutes(5);

        var result = await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = corrected }, fixture.Other);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Service.EndUtc.ShouldBe(corrected);
        result.Value.Service.EndedByUserId.ShouldBe(clear ? null : fixture.Other.UserId);
        result.Value.Service.CreatedByUserId.ShouldBe(fixture.Owner.UserId);
        result.Value.Service.Notes.ShouldBe(fixture.ClosedPerformerInput.Notes);
    }

    [Fact]
    public async Task Creator_cannot_delete_work_closed_by_someone_else()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput)).IsSuccess.ShouldBeTrue();
        (await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = Now.AddMinutes(30) }, fixture.Other))
            .IsSuccess.ShouldBeTrue();

        var result = await fixture.DeleteAsync(id);

        result.IsFailure.ShouldBeTrue();
        (await fixture.ReadAsync()).Services.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creator_can_delete_open_work_or_work_they_closed(bool closed)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var input = fixture.Input with { EndUtc = closed ? Now.AddMinutes(30) : null,
            Performers = closed ? fixture.ClosedPerformerInput.Performers : fixture.Input.Performers };
        (await fixture.SaveAsync(id, input)).IsSuccess.ShouldBeTrue();

        var result = await fixture.DeleteAsync(id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(id);
        (await fixture.ReadAsync()).Services.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-staff")]
    public async Task Invalid_performer_collections_reject_the_whole_service_save_without_mutation(string invalid)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        (await fixture.SaveAsync(id)).IsSuccess.ShouldBeTrue();
        var original = fixture.Input.Performers.Single();
        IReadOnlyList<SaveFlightServicePerformerRequest> performers = invalid switch
        {
            "none" => [],
            "duplicate-id" => [original, original with { StaffMemberId = fixture.Other.StaffMemberId }],
            _ => [original, original with { Id = Guid.NewGuid() }]
        };
        var writes = fixture.Writes.SaveCalls;

        var result = await fixture.SaveAsync(id, fixture.Input with { Notes = "Must not leak", Performers = performers });

        result.IsFailure.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(writes);
        var service = (await fixture.ReadAsync()).Services.ShouldHaveSingleItem();
        service.Notes.ShouldBe(fixture.Input.Notes);
        service.Performers.ShouldHaveSingleItem().Id.ShouldBe(original.Id);
    }

    [Fact]
    public async Task Exact_retry_with_old_version_and_same_media_does_not_add_a_second_blob_or_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var input = fixture.Input with { Attachments = [NewImage()] };
        var saved = await fixture.SaveAsync(id, input, version: InitialVersion);
        saved.IsSuccess.ShouldBeTrue();
        await fixture.ChangeVersionAsync();
        var writes = fixture.Writes.SaveCalls;

        var retry = await fixture.SaveAsync(id, input, version: InitialVersion);

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.Service.Id.ShouldBe(id);
        retry.Value.Service.Attachments.ShouldHaveSingleItem().Id.ShouldBe(input.Attachments!.Single().Id);
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.SaveCalls.ShouldBe(1);
        fixture.Storage.Files.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Changed_payload_with_old_version_returns_conflict_before_uploading_or_mutating()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        (await fixture.SaveAsync(id)).IsSuccess.ShouldBeTrue();
        await fixture.ChangeVersionAsync();
        var writes = fixture.Writes.SaveCalls;

        var result = await fixture.SaveAsync(id, fixture.Input with { Notes = "Stale correction", Attachments = [NewImage()] },
            version: InitialVersion);

        result.Error.Code.ShouldBe("General.ConcurrencyConflict");
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.Files.ShouldBeEmpty();
        (await fixture.ReadAsync()).Services.ShouldHaveSingleItem().Notes.ShouldBe(fixture.Input.Notes);
    }

    [Fact]
    public async Task Null_attachments_preserve_existing_media_and_empty_attachments_remove_it()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var attachment = NewImage();
        (await fixture.SaveAsync(id, fixture.Input with { Attachments = [attachment] })).IsSuccess.ShouldBeTrue();

        var preserved = await fixture.SaveAsync(id, fixture.Input with { Notes = "Core correction", Attachments = null });

        preserved.IsSuccess.ShouldBeTrue();
        preserved.Value.Service.Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachment.Id);
        fixture.Storage.Files.ShouldHaveSingleItem();

        var removed = await fixture.SaveAsync(id, fixture.Input with { Notes = "Core correction", Attachments = [] });

        removed.IsSuccess.ShouldBeTrue();
        removed.Value.Service.Attachments.ShouldBeEmpty();
        fixture.Storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Existing_attachment_identity_cannot_be_reused_for_replacement_bytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var attachment = NewImage();
        (await fixture.SaveAsync(id, fixture.Input with { Attachments = [attachment] })).IsSuccess.ShouldBeTrue();
        var replacement = attachment with { Content = [.. Png, 0x01] };
        var writes = fixture.Writes.SaveCalls;

        var result = await fixture.SaveAsync(id, fixture.Input with { Attachments = [replacement] });

        result.IsFailure.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.SaveCalls.ShouldBe(1);
        fixture.Storage.Files.ShouldHaveSingleItem().Value.ShouldBe(Png);
        (await fixture.ReadAsync()).Services.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachment.Id);
    }

    [Fact]
    public async Task Mime_case_normalization_retains_an_existing_attachment_for_exact_acknowledgment_and_core_correction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var attachment = NewImage() with { ContentType = "IMAGE/PNG" };
        (await fixture.SaveAsync(id, fixture.Input with { Attachments = [attachment] })).IsSuccess.ShouldBeTrue();
        var storageKey = fixture.Storage.Files.Keys.Single();
        var retained = attachment with { ContentType = "image/png", Content = null };
        var sameInput = fixture.Input with { Attachments = [retained] };
        var writes = fixture.Writes.SaveCalls;

        var acknowledgment = await fixture.SaveAsync(id, sameInput);

        acknowledgment.IsSuccess.ShouldBeTrue();
        acknowledgment.Value.Service.Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachment.Id);
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.SaveCalls.ShouldBe(1);

        var corrected = await fixture.SaveAsync(id, sameInput with { Notes = "Core correction with normalized media metadata" });

        corrected.IsSuccess.ShouldBeTrue();
        corrected.Value.Service.Notes.ShouldBe("Core correction with normalized media metadata");
        corrected.Value.Service.Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachment.Id);
        fixture.Storage.SaveCalls.ShouldBe(1);
        fixture.Storage.Files.ShouldHaveSingleItem().Key.ShouldBe(storageKey);
        fixture.Storage.Files.Single().Value.ShouldBe(Png);
        fixture.Storage.DeleteTokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unexpected_save_failure_cleans_unreferenced_new_bytes_and_keeps_the_original_error()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failure = new InvalidOperationException("Injected service save failure");
        fixture.Writes.Save = _ => throw failure;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => fixture.SaveAsync(Guid.NewGuid(),
            fixture.Input with { Attachments = [NewImage()] }));

        thrown.ShouldBeSameAs(failure);
        fixture.Storage.Files.ShouldBeEmpty();
        fixture.Storage.DeleteTokens.ShouldHaveSingleItem().CanBeCanceled.ShouldBeFalse();
    }

    [Fact]
    public async Task Ambiguous_commit_retains_referenced_media_and_exact_replay_acknowledges_the_saved_service()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var input = fixture.Input with { Attachments = [NewImage()] };
        fixture.Writes.Save = async _ =>
        {
            await fixture.Db.SaveChangesAsync(CancellationToken.None);
            throw new InvalidOperationException("Injected lost service response after commit");
        };

        await Should.ThrowAsync<InvalidOperationException>(() => fixture.SaveAsync(id, input));

        fixture.Storage.Files.ShouldHaveSingleItem();
        fixture.Storage.DeleteTokens.ShouldBeEmpty();
        fixture.Db.ChangeTracker.Clear();
        fixture.Writes.Save = null;
        var retry = await fixture.SaveAsync(id, input, version: InitialVersion);

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.Service.Id.ShouldBe(id);
        fixture.Storage.Files.ShouldHaveSingleItem();
        fixture.Storage.SaveCalls.ShouldBe(1);
        fixture.Writes.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrency_failure_releases_only_new_unreferenced_media_and_returns_conflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Writes.Save = _ => throw new DbUpdateConcurrencyException("Injected service race");

        var result = await fixture.SaveAsync(Guid.NewGuid(), fixture.Input with { Attachments = [NewImage()] });

        result.Error.Code.ShouldBe("General.ConcurrencyConflict");
        fixture.Storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ambiguous_delete_commit_releases_unreferenced_media_and_old_version_replay_acknowledges_without_another_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        (await fixture.SaveAsync(id, fixture.Input with { Attachments = [NewImage()] })).IsSuccess.ShouldBeTrue();
        var oldVersion = (await fixture.Db.Flights.SingleAsync(f => f.Id == fixture.FlightId)).RowVersion.ToArray();
        var failure = new InvalidOperationException("Injected lost delete response after commit");
        fixture.Writes.Save = async _ =>
        {
            await fixture.Db.SaveChangesAsync(CancellationToken.None);
            throw failure;
        };

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => fixture.DeleteAsync(id, version: oldVersion));

        thrown.ShouldBeSameAs(failure);
        fixture.Storage.Files.ShouldBeEmpty();
        fixture.Storage.DeleteTokens.ShouldHaveSingleItem().CanBeCanceled.ShouldBeFalse();
        fixture.Db.ChangeTracker.Clear();
        (await fixture.ReadAsync()).Services.ShouldBeEmpty();
        await fixture.ChangeVersionAsync(); // A later flight edit must not break the exact deletion acknowledgement.
        fixture.Writes.Save = null;
        var writes = fixture.Writes.SaveCalls;

        var replay = await fixture.DeleteAsync(id, version: oldVersion);

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.Id.ShouldBe(id);
        replay.Value.RowVersion.ShouldNotBe(Convert.ToBase64String(oldVersion));
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.SaveCalls.ShouldBe(1);
        fixture.Storage.Files.ShouldBeEmpty();
        fixture.Storage.DeleteTokens.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Delete_failure_before_commit_keeps_media_referenced_by_the_persisted_service()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var attachment = NewImage();
        (await fixture.SaveAsync(id, fixture.Input with { Attachments = [attachment] })).IsSuccess.ShouldBeTrue();
        var failure = new InvalidOperationException("Injected delete failure before commit");
        fixture.Writes.Save = _ => throw failure;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => fixture.DeleteAsync(id));

        thrown.ShouldBeSameAs(failure);
        fixture.Storage.Files.ShouldHaveSingleItem().Value.ShouldBe(Png);
        fixture.Storage.DeleteTokens.ShouldBeEmpty();
        var retained = (await fixture.ReadAsync()).Services.ShouldHaveSingleItem();
        retained.Id.ShouldBe(id);
        retained.Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachment.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ambiguous_correction_commit_reclaims_removed_media_preserves_replacement_and_replays_without_extra_work(bool replacement)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var original = NewImage();
        (await fixture.SaveAsync(id, fixture.Input with { Attachments = [original] })).IsSuccess.ShouldBeTrue();
        var originalKey = fixture.Storage.Files.Keys.Single();
        var oldVersion = (await fixture.Db.Flights.SingleAsync(f => f.Id == fixture.FlightId)).RowVersion.ToArray();
        var nextAttachment = NewImage() with { FileName = "replacement.png", Content = [.. Png, 0x01] };
        var correction = fixture.Input with
        {
            Notes = "Committed correction before response loss",
            Attachments = replacement ? [nextAttachment] : []
        };
        var failure = new InvalidOperationException("Injected lost correction response after commit");
        fixture.Writes.Save = async _ =>
        {
            await fixture.Db.SaveChangesAsync(CancellationToken.None);
            throw failure;
        };

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => fixture.SaveAsync(id, correction, version: oldVersion));

        thrown.ShouldBeSameAs(failure);
        fixture.Storage.Files.ContainsKey(originalKey).ShouldBeFalse();
        fixture.Storage.Files.Count.ShouldBe(replacement ? 1 : 0);
        var stored = (await fixture.ReadAsync()).Services.ShouldHaveSingleItem();
        stored.Notes.ShouldBe(correction.Notes);
        if (replacement)
        {
            stored.Attachments.ShouldHaveSingleItem().Id.ShouldBe(nextAttachment.Id);
            fixture.Storage.Files.ShouldHaveSingleItem().Value.ShouldBe(nextAttachment.Content);
        }
        else stored.Attachments.ShouldBeEmpty();
        fixture.Writes.Save = null;
        await fixture.ChangeVersionAsync();
        var writes = fixture.Writes.SaveCalls;
        var blobWrites = fixture.Storage.SaveCalls;

        var retry = await fixture.SaveAsync(id, correction, version: oldVersion);

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.Service.Id.ShouldBe(id);
        retry.Value.Service.Attachments.Count.ShouldBe(replacement ? 1 : 0);
        if (replacement) retry.Value.Service.Attachments.Single().Id.ShouldBe(nextAttachment.Id);
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.SaveCalls.ShouldBe(blobWrites);
        fixture.Storage.Files.Count.ShouldBe(replacement ? 1 : 0);
    }

    [Fact]
    public async Task Active_catalog_service_outside_the_callers_manpower_allowlist_cannot_be_created()
    {
        await using var fixture = await Fixture.CreateAsync();
        var disallowedId = Guid.NewGuid();
        var catalog = await fixture.Reader.GetServicesAsync([disallowedId], CancellationToken.None);
        catalog.ShouldHaveSingleItem().IsActive.ShouldBeTrue();

        var result = await fixture.SaveAsync(Guid.NewGuid(), fixture.Input with
        {
            ServiceId = disallowedId, Attachments = [NewImage()]
        });

        result.Error.Code.ShouldBe("Operations.WorkOrder.ServiceNotAllowed");
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
        (await fixture.ReadAsync()).Services.ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoked_manpower_allowance_blocks_end_only_changes_deletes_and_exact_replays()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var original = fixture.Input with { Attachments = [NewImage()] };
        (await fixture.SaveAsync(id, original)).IsSuccess.ShouldBeTrue();
        var writes = fixture.Writes.SaveCalls;
        fixture.Reader.AllowedServicesOverride = new HashSet<Guid>();

        var endOnly = await fixture.SaveAsync(id, fixture.Input with { EndUtc = Now.AddMinutes(30) });
        var deletion = await fixture.DeleteAsync(id);
        var replay = await fixture.SaveAsync(id, original, version: InitialVersion);

        endOnly.Error.Code.ShouldBe("Operations.WorkOrder.ServiceNotAllowed");
        deletion.Error.Code.ShouldBe("Operations.WorkOrder.ServiceNotAllowed");
        replay.Error.Code.ShouldBe("Operations.WorkOrder.ServiceNotAllowed");
        fixture.Writes.SaveCalls.ShouldBe(writes);
        fixture.Storage.SaveCalls.ShouldBe(1);
        fixture.Storage.Files.ShouldHaveSingleItem();
        var unchanged = (await fixture.ReadAsync()).Services.ShouldHaveSingleItem();
        unchanged.Window.ToUtc.ShouldBeNull();
        unchanged.EndedByUserId.ShouldBeNull();
        unchanged.Attachments.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(false, "Operations.Ref.StaffInactive")]
    [InlineData(true, "Operations.Ref.StaffStationMismatch")]
    public async Task Inactive_or_relocated_performer_selection_is_rejected_before_writes_or_uploads(bool relocated, string code)
    {
        await using var fixture = await Fixture.CreateAsync();
        var staff = fixture.Reader.StaffMembers[fixture.Other.StaffMemberId];
        fixture.Reader.StaffMembers[staff.Id] = relocated
            ? staff with { StationId = Guid.NewGuid() }
            : staff with { IsActive = false };
        var performer = fixture.Input.Performers.Single() with { StaffMemberId = staff.Id };

        var result = await fixture.SaveAsync(Guid.NewGuid(), fixture.Input with
        {
            Performers = [performer], Attachments = [NewImage()]
        });

        result.Error.Code.ShouldBe(code);
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
        (await fixture.ReadAsync()).Services.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_permitted_closer_preserves_unchanged_historical_performer_snapshots_after_deactivation_or_move(bool relocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var saved = await fixture.SaveAsync(id, fixture.ClosedPerformerInput);
        saved.IsSuccess.ShouldBeTrue();
        var original = saved.Value.Service.Performers.Single();
        var staff = fixture.Reader.StaffMembers[original.StaffMemberId];
        fixture.Reader.StaffMembers[staff.Id] = relocated
            ? staff with { StationId = Guid.NewGuid(), FullName = "Moved employee" }
            : staff with { IsActive = false, FullName = "Inactive employee" };

        var result = await fixture.SaveAsync(id, fixture.ClosedPerformerInput with { EndUtc = Now.AddMinutes(30) }, fixture.Other);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Service.CanEdit.ShouldBeFalse();
        result.Value.Service.EndedByUserId.ShouldBe(fixture.Other.UserId);
        result.Value.Service.Performers.ShouldHaveSingleItem().ShouldBe(original);
        var replacement = fixture.ClosedPerformerInput with { Performers = [fixture.ClosedPerformerInput.Performers.Single() with { Id = Guid.NewGuid() }] };
        var rejected = await fixture.SaveAsync(Guid.NewGuid(), replacement, fixture.Other);
        rejected.Error.Code.ShouldBe(relocated ? "Operations.Ref.StaffStationMismatch" : "Operations.Ref.StaffInactive");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_changed_service_after_departure_requires_its_own_service_and_staff_ends(bool serviceEndPresent)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RecordDepartureAsync();
        var input = fixture.Input with { EndUtc = serviceEndPresent ? Now.AddMinutes(30) : null, Attachments = [NewImage()] };
        var result = await fixture.SaveAsync(Guid.NewGuid(), input);
        result.Error.Code.ShouldBe("Operations.FlightService.EndRequiredAfterDeparture");
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
        (await fixture.ReadAsync()).Services.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_new_service_after_departure_can_save_when_its_service_and_staff_periods_are_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RecordDepartureAsync();
        var input = fixture.Input with { EndUtc = Now.AddMinutes(30),
            Performers = [fixture.Input.Performers.Single() with { EndUtc = Now.AddMinutes(20) }] };
        var result = await fixture.SaveAsync(Guid.NewGuid(), input);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Service.EndUtc.ShouldBe(input.EndUtc);
        result.Value.Service.Performers.Single().EndUtc.ShouldBe(input.Performers.Single().EndUtc);
    }

    [Fact]
    public async Task Completing_one_service_after_departure_does_not_block_on_another_creators_legacy_open_work()
    {
        await using var fixture = await Fixture.CreateAsync();
        var repairedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var otherInput = fixture.Input with { Performers = [fixture.Input.Performers.Single() with { Id = Guid.NewGuid() }] };
        (await fixture.SaveAsync(repairedId)).IsSuccess.ShouldBeTrue();
        (await fixture.SaveAsync(otherId, otherInput, fixture.Other)).IsSuccess.ShouldBeTrue();
        await fixture.RecordDepartureAsync();
        var corrected = fixture.Input with { EndUtc = Now.AddMinutes(30), Notes = "Completed legacy work",
            Performers = [fixture.Input.Performers.Single() with { EndUtc = Now.AddMinutes(20) }] };

        var result = await fixture.SaveAsync(repairedId, corrected);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ActiveWorkCount.ShouldBe(1);
        result.Value.CompletedWorkCount.ShouldBe(1);
        var other = (await fixture.ReadAsync()).Services.Single(service => service.Id == otherId);
        other.Window.ToUtc.ShouldBeNull();
        other.Performers.Single().Window.ToUtc.ShouldBeNull();
        await fixture.ChangeVersionAsync();
        var replay = await fixture.SaveAsync(otherId, otherInput, fixture.Other, InitialVersion);
        replay.IsSuccess.ShouldBeTrue(); // Acknowledgment precedes the mutable completion rule.
        var changedOpen = await fixture.SaveAsync(otherId, otherInput with { Notes = "New input still open" }, fixture.Other);
        changedOpen.Error.Code.ShouldBe("Operations.FlightService.EndRequiredAfterDeparture");
    }

    private static SaveFlightServiceAttachmentRequest NewImage() =>
        new(Guid.NewGuid(), (int)AttachmentKind.Image, "photo.png", "image/png", Png);

    private sealed record Actor(Guid StaffMemberId, Guid UserId);

    private sealed class Fixture : IAsyncDisposable
    {
        public required OperationsDbContext Db { get; init; }
        public required WriteBoundary Writes { get; init; }
        public required MemoryStorage Storage { get; init; }
        public required SchedulingMasterDataReader Reader { get; init; }
        public required Guid FlightId { get; init; }
        public required Actor Owner { get; init; }
        public required Actor Other { get; init; }
        public required SaveFlightServiceRequest Input { get; init; }
        public SaveFlightServiceRequest ClosedPerformerInput => Input with
        {
            Performers = [Input.Performers.Single() with { EndUtc = Now.AddMinutes(20) }]
        };

        public async Task<Result<FlightServiceSavedDto>> SaveAsync(Guid recordId, SaveFlightServiceRequest? input = null,
            Actor? actor = null, byte[]? version = null)
        {
            var acting = actor ?? Owner;
            var current = version ?? (await Db.Flights.SingleAsync(f => f.Id == FlightId)).RowVersion;
            var handler = new SaveFlightServiceCommandHandler(Writes,
                new Scope(new(Reader.StationId, acting.StaffMemberId, acting.UserId, Now)),
                new MasterDataResolver(Reader), new Clock(), NoopMobileSyncBroadcaster.Instance, Storage);
            return await handler.Handle(new SaveFlightServiceCommand(FlightId, recordId, input ?? Input, current), CancellationToken.None);
        }

        public async Task<Result<FlightServiceRemovedDto>> DeleteAsync(Guid recordId, Actor? actor = null, byte[]? version = null)
        {
            var acting = actor ?? Owner;
            var current = version ?? (await Db.Flights.SingleAsync(f => f.Id == FlightId)).RowVersion;
            var handler = new DeleteFlightServiceCommandHandler(Writes,
                new Scope(new(Reader.StationId, acting.StaffMemberId, acting.UserId, Now)),
                new Clock(), NoopMobileSyncBroadcaster.Instance, Storage, new MasterDataResolver(Reader));
            return await handler.Handle(new DeleteFlightServiceCommand(FlightId, recordId, current), CancellationToken.None);
        }

        public async Task<Flight> ReadAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Flights.Include(f => f.ServiceRecords).ThenInclude(s => s.Performers)
                .Include(f => f.ServiceRecords).ThenInclude(s => s.Attachments).SingleAsync(f => f.Id == FlightId);
        }

        public async Task RecordDepartureAsync()
        {
            var flight = await ReadAsync();
            var staff = Reader.StaffMembers[Owner.StaffMemberId];
            var employee = new StaffMemberSnapshot(staff.Id, staff.FullName, staff.EmployeeId);
            var signature = CustomerSignature.Create("signature.png", "seed/signature.png", "image/png", Png.Length, Now).Value;
            flight.SubmitDeparture(employee, DepartureDetails.Create(Now.AddHours(1), signature, employee, "Departure").Value,
                Owner.UserId, Now).IsSuccess.ShouldBeTrue();
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task ChangeVersionAsync()
        {
            var flight = await Db.Flights.SingleAsync(f => f.Id == FlightId);
            Db.Entry(flight).Property(f => f.RowVersion).CurrentValue = [2, 0, 0, 0, 0, 0, 0, 0];
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var reader = new SchedulingMasterDataReader();
            var owner = new Actor(Guid.NewGuid(), Guid.NewGuid());
            var other = new Actor(Guid.NewGuid(), Guid.NewGuid());
            reader.StaffMembers[owner.StaffMemberId] = new(owner.StaffMemberId, "Service creator", "E1", reader.StationId, owner.UserId, true);
            reader.StaffMembers[other.StaffMemberId] = new(other.StaffMemberId, "Other operator", "E2", reader.StationId, other.UserId, true);
            var flight = Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Customer"),
                new StationSnapshot(reader.StationId, "RUH", "Riyadh"),
                new OperationTypeSnapshot(Guid.NewGuid(), "Transit"), FlightNumber.Create("SV100", "SV").Value,
                ScheduledTime.Create(Now.AddHours(-1), Now.AddHours(1)).Value, null,
                [new ServiceSnapshot(reader.ServiceId, "Planned service")],
                [new StaffMemberSnapshot(owner.StaffMemberId, "Service creator", "E1"),
                    new StaffMemberSnapshot(other.StaffMemberId, "Other operator", "E2")], owner.UserId, Now).Value;
            db.Flights.Add(flight);
            db.Entry(flight).Property(f => f.RowVersion).CurrentValue = InitialVersion.ToArray();
            await db.SaveChangesAsync();
            return new Fixture
            {
                Db = db, Writes = new WriteBoundary(db), Storage = new MemoryStorage(), Reader = reader,
                FlightId = flight.Id, Owner = owner, Other = other,
                Input = new(reader.ServiceId, Now.AddMinutes(-30), null, "REF-1", "Service notes",
                    [new(Guid.NewGuid(), owner.StaffMemberId, Now.AddMinutes(-20))])
            };
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class WriteBoundary(OperationsDbContext db) : IOperationsDbContext
    {
        public Func<CancellationToken, Task<int>>? Save { get; set; }
        public int SaveCalls { get; private set; }
        public DbSet<Flight> Flights => db.Flights;
        public DbSet<OutboxMessage> OutboxMessages => db.OutboxMessages;
        public DbSet<InboxMessage> InboxMessages => db.InboxMessages;
        public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
            db.SetOriginalRowVersion(entity, rowVersion);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Save?.Invoke(cancellationToken) ?? db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<CancellationToken> DeleteTokens { get; } = [];
        public int SaveCalls { get; private set; }
        public async Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var key = $"{container}/{Guid.NewGuid():N}-{Path.GetFileName(fileName)}";
            Files[key] = buffer.ToArray();
            return new(key, contentType, buffer.Length);
        }
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeleteTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class Scope(MobileFlightScopeContext context) : IMobileFlightScope
    {
        public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
