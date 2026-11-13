using Operations.Contracts;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Resources;
using MasterData.Contracts.Seeding;
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
using System.Text.Json;

namespace Operations.Application.UnitTests;

public sealed class FlightTaskSaveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] InitialVersion = [1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");

    [Fact]
    public async Task Save_roundtrips_stable_children_media_and_normalized_text_and_exact_retry_is_a_noop()
    {
        await using var f = await Fixture.CreateAsync();
        var id = Guid.NewGuid(); var toolId = Guid.NewGuid(); var materialId = Guid.NewGuid(); var supportId = Guid.NewGuid();
        f.Reader.Tools[toolId] = new(toolId, "Tool", true, ResourceCalculationType.Duration);
        f.Reader.Materials[materialId] = new(materialId, "Material", true, ResourceCalculationType.Quantity);
        f.Reader.GeneralSupports[supportId] = new(supportId, "Support", true, ResourceCalculationType.Quantity);
        var image = Image();
        var input = f.Input with { Notes = " Corrected ", CustomerReferenceNumber = " ref ", Attachments = [image],
            Tools = [new(Guid.NewGuid(), toolId, StartUtc: Now.AddMinutes(-20))],
            Materials = [new(Guid.NewGuid(), materialId, 9999999999999999.99m)],
            GeneralSupports = [new(Guid.NewGuid(), supportId, 1.25m)] };
        var saved = await f.SaveAsync(id, input);
        saved.IsSuccess.ShouldBeTrue(); saved.Value.Task.Id.ShouldBe(id);
        saved.Value.Task.Materials.Single().Quantity.ShouldBe(9999999999999999.99m);
        saved.Value.Task.Notes.ShouldBe("Corrected"); saved.Value.Task.CustomerReferenceNumber.ShouldBe("REF");
        saved.Value.Task.Tools.Single().Id.ShouldBe(input.Tools.Single().Id);
        saved.Value.Task.Performers.Single().Id.ShouldBe(input.Performers.Single().Id);
        saved.Value.Task.Attachments.Single().Id.ShouldBe(image.Id);
        saved.Value.ActiveWorkCount.ShouldBe(1); saved.Value.CompletedWorkCount.ShouldBe(0);
        f.Storage.SaveCalls.ShouldBe(1); var writes = f.Writes.SaveCalls;
        await f.ChangeVersionAsync();
        (await f.SaveAsync(id, input, version: InitialVersion)).IsSuccess.ShouldBeTrue();
        f.Writes.SaveCalls.ShouldBe(writes); f.Storage.SaveCalls.ShouldBe(1);
        var retained = await f.ReadAsync(); var task = retained.Tasks.Single(); var tool = task.Tools.Single();
        var correction = input with { Notes = "Next", Attachments = null, Materials = [input.Materials.Single() with { Quantity = 2.50m }] };
        (await f.SaveAsync(id, correction)).IsSuccess.ShouldBeTrue();
        var result = (await f.ReadAsync()).Tasks.ShouldHaveSingleItem();
        result.Id.ShouldBe(id); result.Tools.Single().Id.ShouldBe(tool.Id);
        result.Materials.Single().Usage.Quantity.ShouldBe(2.5m); result.Attachments.ShouldHaveSingleItem().Id.ShouldBe(image.Id);
        var json = JsonSerializer.Serialize(saved.Value.Task, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        JsonDocument.Parse(json).RootElement.GetProperty("materials")[0].GetProperty("quantity").GetString().ShouldBe("9999999999999999.99");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Each_task_type_requires_unique_station_staff(int type)
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid();
        (await f.SaveAsync(id, f.Input with { TaskType = type, Performers = [] })).Error.Code.ShouldBe("Operations.WorkOrder.TaskPerformerRequired");
        var p = f.Input.Performers.Single();
        (await f.SaveAsync(id, f.Input with { TaskType = type, Performers = [p, p with { Id = Guid.NewGuid() }] }))
            .Error.Code.ShouldBe("Operations.FlightTask.PerformersInvalid");
        f.Reader.StaffMembers[p.StaffMemberId] = f.Reader.StaffMembers[p.StaffMemberId] with { StationId = Guid.NewGuid() };
        (await f.SaveAsync(id, f.Input with { TaskType = type })).Error.Code.ShouldBe("Operations.Ref.StaffStationMismatch");
        (await f.ReadAsync()).Tasks.ShouldBeEmpty(); f.Storage.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("10000000000000000")]
    public async Task Quantity_resource_rejects_nonpositive_or_out_of_precision_values(string quantity)
    {
        await using var f = await Fixture.CreateAsync(); var resourceId = Guid.NewGuid();
        f.Reader.Materials[resourceId] = new(resourceId, "Material", true);
        var input = f.Input with { Materials = [new(Guid.NewGuid(), resourceId, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture))] };
        (await f.SaveAsync(Guid.NewGuid(), input)).IsFailure.ShouldBeTrue();
        (await f.ReadAsync()).Tasks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Resource_snapshot_controls_usage_branch_and_unknown_resource_requires_notes()
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid(); var toolId = Guid.NewGuid();
        f.Reader.Tools[toolId] = new(toolId, "Quantity tool", true, ResourceCalculationType.Quantity);
        var row = new SaveFlightTaskResourceRequest(Guid.NewGuid(), toolId, 1);
        (await f.SaveAsync(id, f.Input with { Tools = [row with { StartUtc = Now }] })).Error.Code.ShouldBe("Operations.ResourceUsage.QuantityHasDuration");
        (await f.SaveAsync(id, f.Input with { Tools = [row, row with { Id = Guid.NewGuid() }] })).Error.Code.ShouldBe("Operations.FlightTask.ResourcesInvalid");
        var unknown = WellKnownMasterDataIds.UnknownMaterial;
        f.Reader.Materials[unknown] = new(unknown, "Unknown", true);
        (await f.SaveAsync(id, f.Input with { Materials = [new(Guid.NewGuid(), unknown, 1)] })).Error.Code.ShouldBe("Operations.WorkOrder.UnknownDescriptionRequired");
        (await f.SaveAsync(id, f.Input with { Tools = [row], Materials = [new(Guid.NewGuid(), unknown, 1, Notes: "Specified material")] })).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Duration_resources_can_save_open_but_task_closure_and_departure_require_their_ends()
    {
        await using var f = await Fixture.CreateAsync(); var toolId = Guid.NewGuid(); var id = Guid.NewGuid();
        f.Reader.Tools[toolId] = new(toolId, "Tool", true, ResourceCalculationType.Duration);
        var input = f.Input with { Tools = [new(Guid.NewGuid(), toolId, StartUtc: Now)] };
        (await f.SaveAsync(id, input)).IsSuccess.ShouldBeTrue();
        var closing = input with { EndUtc = Now.AddMinutes(30), Performers = f.ClosedPerformerInput.Performers };
        var rejected = await f.SaveAsync(id, closing);
        rejected.Error.Code.ShouldBe("Operations.FlightTask.OpenResourcePeriods");
        rejected.Error.Failures!.Keys.ShouldContain("Tools[0].EndUtc"); rejected.Error.Failures.Keys.ShouldContain("EndUtc");
        var completed = closing with { Tools = [closing.Tools.Single() with { EndUtc = Now.AddMinutes(10) }] };
        (await f.SaveAsync(id, completed)).IsSuccess.ShouldBeTrue();
        await f.RecordDepartureAsync();
        var invalidAfterDeparture = closing with { Performers = [closing.Performers.Single() with { Id = Guid.NewGuid() }], Tools = [closing.Tools.Single() with { Id = Guid.NewGuid() }] };
        (await f.SaveAsync(Guid.NewGuid(), invalidAfterDeparture)).Error.Code.ShouldBe("Operations.FlightTask.EndRequiredAfterDeparture");
    }

    [Fact]
    public async Task Closing_task_with_open_staff_returns_field_errors_before_writing()
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid();
        var result = await f.SaveAsync(id, f.Input with { EndUtc = Now.AddMinutes(30), Attachments = [Image()] });
        result.Error.Code.ShouldBe("Operations.FlightTask.OpenPerformerPeriods");
        result.Error.Failures!.Keys.ShouldContain("EndUtc"); result.Error.Failures.Keys.ShouldContain("Performers[0].EndUtc");
        f.Writes.SaveCalls.ShouldBe(0); f.Storage.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Unassigned_searcher_can_close_foreign_work_while_creator_keeps_ordinary_core_rights()
    {
        await using var f = await Fixture.CreateAsync(assigned: false); var id = Guid.NewGuid(); var input = f.ClosedPerformerInput;
        (await f.SaveAsync(id, input)).IsSuccess.ShouldBeTrue();
        (await f.SaveAsync(id, input with { Notes = "Foreign edit" }, f.Other)).Error.Code.ShouldBe("Operations.Search.ClosureReadOnly");
        input = input with { EndUtc = Now.AddMinutes(30) };
        var closed = await f.SaveAsync(id, input, f.Other);
        closed.IsSuccess.ShouldBeTrue(); closed.Value.Task.CanEdit.ShouldBeFalse(); closed.Value.Task.CanSetEnd.ShouldBeFalse();
        var mine = await f.SaveAsync(id, input with { Notes = "Owner correction" });
        mine.IsSuccess.ShouldBeTrue(); mine.Value.Task.CanEdit.ShouldBeTrue(); mine.Value.Task.CanSetEnd.ShouldBeFalse(); mine.Value.Task.CanChangeType.ShouldBeTrue();
        (await f.SaveAsync(id, input with { TaskType = 1, Notes = "Owner correction" })).IsSuccess.ShouldBeTrue();
        (await f.SaveAsync(id, input with { EndUtc = null })).Error.Code.ShouldBe("Operations.FlightTask.EndReadOnly");
        (await f.DeleteAsync(id)).Error.Code.ShouldBe("Operations.FlightTask.DeleteReadOnly");
        (await f.SaveAsync(id, input with { EndUtc = null, TaskType = 1, Notes = "Owner correction" }, f.Other)).Error.Code.ShouldBe("Operations.Search.ClosureReadOnly");
        (await f.ReadAsync()).Tasks.Single().Window.ToUtc.ShouldBe(input.EndUtc);
    }

    [Fact]
    public async Task Saved_snapshots_are_retained_after_reference_deactivation_but_new_selections_require_active_catalogs()
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid(); var toolId = Guid.NewGuid(); var chapterId = Guid.NewGuid();
        f.Reader.Tools[toolId] = new(toolId, "Captured name", true, ResourceCalculationType.Quantity);
        f.Reader.AtaChapters[chapterId] = new(chapterId, Guid.NewGuid(), "Category", "21", "Original chapter", true, true);
        var input = f.Input with { AtaChapterId = chapterId, Tools = [new(Guid.NewGuid(), toolId, 1)] };
        (await f.SaveAsync(id, input)).IsSuccess.ShouldBeTrue();
        f.Reader.Tools[toolId] = f.Reader.Tools[toolId] with { IsActive = false, Name = "Renamed", CalculationType = ResourceCalculationType.Duration };
        f.Reader.AtaChapters[chapterId] = f.Reader.AtaChapters[chapterId] with { IsActive = false, Title = "Changed" };
        var p = input.Performers.Single(); f.Reader.StaffMembers[p.StaffMemberId] = f.Reader.StaffMembers[p.StaffMemberId] with { IsActive = false };
        var result = await f.SaveAsync(id, input with { Notes = "Correction" });
        result.IsSuccess.ShouldBeTrue(); result.Value.Task.Tools.Single().Name.ShouldBe("Captured name");
        result.Value.Task.Tools.Single().CalculationType.ShouldBe(0); result.Value.Task.AtaChapterTitle.ShouldBe("Original chapter");
        (await f.SaveAsync(Guid.NewGuid(), input with { Performers = [p with { Id = Guid.NewGuid() }], Tools = [input.Tools.Single() with { Id = Guid.NewGuid() }] })).Error.Code.ShouldBe("Operations.Ref.AtaChapterInactive");
    }

    [Fact]
    public async Task Changed_stale_input_rejects_without_upload_and_delete_retry_is_idempotent()
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid(); var image = Image();
        (await f.SaveAsync(id, f.Input with { Attachments = [image] })).IsSuccess.ShouldBeTrue();
        await f.ChangeVersionAsync(); var writes = f.Writes.SaveCalls;
        (await f.SaveAsync(id, f.Input with { Notes = "Stale", Attachments = [Image()] }, version: InitialVersion)).Error.Code.ShouldBe("General.ConcurrencyConflict");
        f.Writes.SaveCalls.ShouldBe(writes); f.Storage.SaveCalls.ShouldBe(1);
        (await f.DeleteAsync(id)).IsSuccess.ShouldBeTrue(); f.Storage.Files.ShouldBeEmpty();
        (await f.DeleteAsync(id, version: InitialVersion)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Attachment_ids_are_immutable_and_null_retains_while_empty_removes()
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid(); var image = Image();
        (await f.SaveAsync(id, f.Input with { Attachments = [image] })).IsSuccess.ShouldBeTrue();
        (await f.SaveAsync(id, f.Input with { Attachments = [image with { Content = Png.Concat(new byte[] { 1 }).ToArray() }] })).IsFailure.ShouldBeTrue();
        (await f.SaveAsync(id, f.Input with { Notes = "Keep", Attachments = null })).IsSuccess.ShouldBeTrue();
        (await f.ReadAsync()).Tasks.Single().Attachments.ShouldHaveSingleItem();
        (await f.SaveAsync(id, f.Input with { Attachments = [] })).IsSuccess.ShouldBeTrue();
        (await f.ReadAsync()).Tasks.Single().Attachments.ShouldBeEmpty(); f.Storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Exact_retry_acknowledges_legacy_closed_task_with_open_resource_before_new_completion_rule()
    {
        await using var f = await Fixture.CreateAsync(); var toolId = Guid.NewGuid(); var id = Guid.NewGuid();
        f.Reader.Tools[toolId] = new(toolId, "Tool", true, ResourceCalculationType.Duration);
        var input = f.ClosedPerformerInput with { Tools = [new(Guid.NewGuid(), toolId, StartUtc: Now.AddHours(2))] };
        (await f.SaveAsync(id, input)).IsSuccess.ShouldBeTrue();
        var flight = await f.ReadAsync(); var task = flight.Tasks.Single();
        typeof(FlightTask).GetProperty(nameof(FlightTask.Window))!.SetValue(task, TimeWindow.Create(input.StartUtc, Now.AddMinutes(30)).Value);
        typeof(FlightTask).GetProperty(nameof(FlightTask.EndedByUserId))!.SetValue(task, f.Owner.UserId);
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear(); await f.ChangeVersionAsync();
        var legacy = input with { EndUtc = Now.AddMinutes(30) }; var writes = f.Writes.SaveCalls;
        (await f.SaveAsync(id, legacy, version: InitialVersion)).IsSuccess.ShouldBeTrue(); f.Writes.SaveCalls.ShouldBe(writes);
        (await f.SaveAsync(id, legacy with { Notes = "New correction" })).Error.Code.ShouldBe("Operations.FlightTask.OpenResourcePeriods");
    }

    [Fact]
    public async Task Body_quantity_strings_and_numbers_deserialize_exactly_without_floating_point_loss()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var token in new[] { "\"9999999999999999.99\"", "9999999999999999.99" })
        {
            var raw = "{\"id\":\"" + Guid.NewGuid() + "\",\"resourceId\":\"" + Guid.NewGuid() + "\",\"quantity\":" + token + "}";
            JsonSerializer.Deserialize<SaveFlightTaskResourceRequest>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!
                .Quantity.ShouldBe(9999999999999999.99m);
        }
    }

    private static SaveFlightServiceAttachmentRequest Image() => new(Guid.NewGuid(), 0, "photo.png", "image/png", Png);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assigned_employee_fully_corrects_active_or_done_task_with_resources_media_and_original_creator(bool done)
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid(); var materialId = Guid.NewGuid(); var toolId = Guid.NewGuid();
        f.Reader.Materials[materialId] = new(materialId, "Quantity material", true, ResourceCalculationType.Quantity);
        f.Reader.Tools[toolId] = new(toolId, "Duration tool", true, ResourceCalculationType.Duration);
        var input = f.ClosedPerformerInput with { EndUtc = done ? Now.AddMinutes(30) : null, Attachments = [Image()],
            Materials = [new(Guid.NewGuid(), materialId, 1, Notes: "Original")],
            Tools = [new(Guid.NewGuid(), toolId, StartUtc: Now, EndUtc: Now.AddMinutes(10))] };
        var initial = await f.SaveAsync(id, input); initial.IsSuccess.ShouldBeTrue();
        (await f.Db.OutboxMessages.CountAsync()).ShouldBe(0);
        var changed = input with { TaskType = 1, Notes = "Assigned correction", CustomerReferenceNumber = "CORRECTED", Attachments = [Image()],
            Materials = [input.Materials.Single() with { Quantity = 2.5m, Notes = "Corrected resource" }],
            Performers = [input.Performers.Single() with { Notes = "Corrected staff", StartUtc = Now }] };

        var result = await f.SaveAsync(id, changed, f.Other);

        result.IsSuccess.ShouldBeTrue(); var dto = result.Value.Task;
        dto.Id.ShouldBe(id); dto.CreatedByUserId.ShouldBe(f.Owner.UserId); dto.CreatedAtUtc.ShouldBe(initial.Value.Task.CreatedAtUtc);
        dto.CreatedByDisplayName.ShouldBe("Original creator account"); dto.UpdatedByUserId.ShouldBe(f.Other.UserId);
        dto.CanEdit.ShouldBeTrue(); dto.CanChangeType.ShouldBeTrue(); dto.CanSetEnd.ShouldBeTrue(); dto.CanDelete.ShouldBeTrue();
        dto.EndedByUserId.ShouldBe(done ? f.Owner.UserId : null); dto.Materials.Single().Quantity.ShouldBe(2.5m);
        dto.Performers.Single().Id.ShouldBe(input.Performers.Single().Id); dto.Performers.Single().Notes.ShouldBe("Corrected staff");
        f.Storage.Files.ShouldHaveSingleItem();
        var notice = JsonSerializer.Deserialize<FlightSubmissionUpdated>((await f.Db.OutboxMessages.SingleAsync()).Content)!;
        notice.SubmissionKind.ShouldBe(FlightSubmissionKind.Task); notice.SubmittedByUserId.ShouldBe(f.Owner.UserId);
        notice.SubmittedByStaffMemberId.ShouldBeNull(); notice.UpdatedByDisplayName.ShouldBe("Assigned editor account");
        var saves = f.Writes.SaveCalls; var uploads = f.Storage.SaveCalls; await f.ChangeVersionAsync();
        f.Names.Values[f.Owner.UserId] = "Renamed creator account";
        (await f.SaveAsync(id, changed, f.Other, InitialVersion)).Value.Task.CreatedByDisplayName.ShouldBe("Renamed creator account");
        f.Writes.SaveCalls.ShouldBe(saves); f.Storage.SaveCalls.ShouldBe(uploads); (await f.Db.OutboxMessages.CountAsync()).ShouldBe(1);
        (await f.SaveAsync(id, changed with { Notes = "Stale edit" }, f.Other, InitialVersion)).Error.Code.ShouldBe("General.ConcurrencyConflict");
        var invalid = changed with { Materials = [changed.Materials.Single() with { Quantity = 0 }] };
        (await f.SaveAsync(id, invalid, f.Other)).IsFailure.ShouldBeTrue(); (await f.Db.OutboxMessages.CountAsync()).ShouldBe(1);
        (await f.SaveAsync(id, changed with { EndUtc = Now.AddMinutes(35) }, f.Other)).Value.Task.EndedByUserId.ShouldBe(f.Other.UserId);
        (await f.DeleteAsync(id, f.Other)).IsSuccess.ShouldBeTrue();
        (await f.Db.OutboxMessages.OrderBy(item => item.OccurredOnUtc).ToListAsync()).Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_task_correction_failure_or_lost_response_does_not_duplicate_notification(bool committed)
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid();
        (await f.SaveAsync(id)).IsSuccess.ShouldBeTrue(); var changed = f.Input with { Notes = "Assigned correction" };
        f.Writes.Save = committed ? async ct => { await f.Db.SaveChangesAsync(ct); throw new IOException("Response loss"); }
            : _ => throw new DbUpdateConcurrencyException("Stale write");
        if (committed) await Should.ThrowAsync<IOException>(() => f.SaveAsync(id, changed, f.Other));
        else (await f.SaveAsync(id, changed, f.Other)).Error.Code.ShouldBe("General.ConcurrencyConflict");
        f.Db.ChangeTracker.Clear(); (await f.Db.OutboxMessages.CountAsync()).ShouldBe(committed ? 1 : 0);
        if (committed) (await f.SaveAsync(id, changed, f.Other, InitialVersion)).IsSuccess.ShouldBeTrue();
        (await f.Db.OutboxMessages.CountAsync()).ShouldBe(committed ? 1 : 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Assigned_foreign_delete_active_or_done_notifies_creator_once_even_after_response_loss(bool done, bool responseLoss)
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid();
        var input = f.ClosedPerformerInput with { EndUtc = done ? Now.AddMinutes(30) : null, Attachments = [Image()] };
        (await f.SaveAsync(id, input)).IsSuccess.ShouldBeTrue();
        if (responseLoss) f.Writes.Save = async ct => { await f.Db.SaveChangesAsync(ct); throw new IOException("Response loss"); };
        if (responseLoss) await Should.ThrowAsync<IOException>(() => f.DeleteAsync(id, f.Other));
        else (await f.DeleteAsync(id, f.Other)).IsSuccess.ShouldBeTrue();
        f.Db.ChangeTracker.Clear();
        var notice = JsonSerializer.Deserialize<FlightSubmissionUpdated>((await f.Db.OutboxMessages.SingleAsync()).Content)!;
        notice.Action.ShouldBe(FlightSubmissionAction.Deleted); notice.SubmittedByUserId.ShouldBe(f.Owner.UserId);
        notice.SubmissionId.ShouldBe(id); notice.SubmissionKind.ShouldBe(FlightSubmissionKind.Task);
        notice.UpdatedByDisplayName.ShouldBe("Assigned editor account");
        f.Storage.Files.ShouldBeEmpty();
        (await f.DeleteAsync(id, f.Other, InitialVersion)).IsSuccess.ShouldBeTrue();
        (await f.Db.OutboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Rejected_foreign_delete_preserves_record_media_and_has_no_notification()
    {
        await using var f = await Fixture.CreateAsync(); var id = Guid.NewGuid();
        (await f.SaveAsync(id, f.Input with { Attachments = [Image()] })).IsSuccess.ShouldBeTrue();
        await f.ChangeVersionAsync();
        (await f.DeleteAsync(id, f.Other, InitialVersion)).Error.Code.ShouldBe("General.ConcurrencyConflict");
        f.Writes.Save = _ => throw new DbUpdateConcurrencyException("Injected race");
        (await f.DeleteAsync(id, f.Other)).Error.Code.ShouldBe("General.ConcurrencyConflict");
        f.Db.ChangeTracker.Clear();
        (await f.Db.OutboxMessages.CountAsync()).ShouldBe(0); f.Storage.Files.ShouldHaveSingleItem();
        (await f.ReadAsync()).Tasks.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task New_task_requires_arrival_without_publishing_work()
    {
        await using var f = await Fixture.CreateAsync(withArrival: false);
        (await f.SaveAsync(Guid.NewGuid())).Error.Code.ShouldBe("Operations.Flight.ArrivalRequired");
        f.Writes.SaveCalls.ShouldBe(0); (await f.ReadAsync()).Tasks.ShouldBeEmpty();
    }

    private sealed record Actor(Guid StaffMemberId, Guid UserId);

    private sealed class Fixture : IAsyncDisposable
    {
        public required OperationsDbContext Db { get; init; }
        public required WriteBoundary Writes { get; init; }
        public required MemoryStorage Storage { get; init; }
        public required SchedulingMasterDataReader Reader { get; init; }
        public required WorkUserNames Names { get; init; }
        public required Guid FlightId { get; init; }
        public required Actor Owner { get; init; }
        public required Actor Other { get; init; }
        private bool RemoveCreatorAssignmentAfterFirstSave { get; set; }
        public required SaveFlightTaskRequest Input { get; init; }
        public SaveFlightTaskRequest ClosedPerformerInput => Input with
        {
            Performers = [Input.Performers.Single() with { EndUtc = Now.AddMinutes(20) }]
        };

        public async Task<Result<FlightTaskSavedDto>> SaveAsync(Guid recordId, SaveFlightTaskRequest? input = null,
            Actor? actor = null, byte[]? version = null)
        {
            var acting = actor ?? Owner;
            var current = version ?? (await Db.Flights.SingleAsync(f => f.Id == FlightId)).RowVersion;
            var handler = new SaveFlightTaskCommandHandler(Writes,
                new Scope(new(Reader.StationId, acting.StaffMemberId, acting.UserId, Now)),
                new MasterDataResolver(Reader), Reader, new Clock(), NoopMobileSyncBroadcaster.Instance, Storage, Names);
            var result = await handler.Handle(new SaveFlightTaskCommand(FlightId, recordId, input ?? Input, current), CancellationToken.None);
            if (result.IsSuccess && RemoveCreatorAssignmentAfterFirstSave)
            {
                // Exercise retained own-record rights after a real scheduling assignment is removed.
                RemoveCreatorAssignmentAfterFirstSave = false;
                var flight = await Db.Flights.Include(item => item.AssignedEmployees).SingleAsync(item => item.Id == FlightId);
                flight.ReplaceAssignedEmployees([], Now).IsSuccess.ShouldBeTrue();
                await Db.SaveChangesAsync();
            }
            return result;
        }

        public async Task<Result<FlightTaskRemovedDto>> DeleteAsync(Guid recordId, Actor? actor = null, byte[]? version = null)
        {
            var acting = actor ?? Owner;
            var current = version ?? (await Db.Flights.SingleAsync(f => f.Id == FlightId)).RowVersion;
            var handler = new DeleteFlightTaskCommandHandler(Writes,
                new Scope(new(Reader.StationId, acting.StaffMemberId, acting.UserId, Now)),
                new Clock(), NoopMobileSyncBroadcaster.Instance, Storage, Names);
            return await handler.Handle(new DeleteFlightTaskCommand(FlightId, recordId, current), CancellationToken.None);
        }

        public async Task<Flight> ReadAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Flights.Include(f => f.TaskRecords).ThenInclude(s => s.Performers)
                .Include(f => f.TaskRecords).ThenInclude(s => s.Attachments)
                .Include(f => f.TaskRecords).ThenInclude(t => t.Tools)
                .Include(f => f.TaskRecords).ThenInclude(t => t.Materials)
                .Include(f => f.TaskRecords).ThenInclude(t => t.GeneralSupports).SingleAsync(f => f.Id == FlightId);
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

        public static async Task<Fixture> CreateAsync(bool assigned = true, bool withArrival = true)
        {
            var db = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var reader = new SchedulingMasterDataReader();
            var owner = new Actor(Guid.NewGuid(), Guid.NewGuid());
            var other = new Actor(Guid.NewGuid(), Guid.NewGuid());
            reader.StaffMembers[owner.StaffMemberId] = new(owner.StaffMemberId, "Task creator", "E1", reader.StationId, owner.UserId, true);
            reader.StaffMembers[other.StaffMemberId] = new(other.StaffMemberId, "Other operator", "E2", reader.StationId, other.UserId, true);
            var flight = Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Customer"),
                new StationSnapshot(reader.StationId, "RUH", "Riyadh"),
                new OperationTypeSnapshot(Guid.NewGuid(), "Transit"), FlightNumber.Create("SV100", "SV").Value,
                ScheduledTime.Create(Now.AddHours(-1), Now.AddHours(1)).Value, null,
                [new ServiceSnapshot(reader.ServiceId, "Planned service")],
                assigned ? [new StaffMemberSnapshot(owner.StaffMemberId, "Task creator", "E1"),
                    new StaffMemberSnapshot(other.StaffMemberId, "Other operator", "E2")]
                    : [new StaffMemberSnapshot(owner.StaffMemberId, "Task creator", "E1")], owner.UserId, Now).Value;
            if (withArrival)
            {
                var employee = new StaffMemberSnapshot(owner.StaffMemberId, "Receiving employee", "E1");
                flight.SubmitArrival(employee, ArrivalDetails.Create(null, Now.AddHours(-2),
                    new AircraftTypeSnapshot(Guid.NewGuid(), "Boeing", "737"), "HZ-TEST", employee, null).Value,
                    owner.UserId, Now.AddHours(-2)).IsSuccess.ShouldBeTrue();
            }
            db.Flights.Add(flight);
            db.Entry(flight).Property(f => f.RowVersion).CurrentValue = InitialVersion.ToArray();
            await db.SaveChangesAsync();
            return new Fixture
            {
                Db = db, Writes = new WriteBoundary(db), Storage = new MemoryStorage(), Reader = reader,
                FlightId = flight.Id, Owner = owner, Other = other, RemoveCreatorAssignmentAfterFirstSave = !assigned,
                Names = new(new Dictionary<Guid, string> { [owner.UserId] = "Original creator account", [other.UserId] = "Assigned editor account" }),
                Input = new(0, null, Now.AddMinutes(-30), null, "REF-1", "Task notes",
                    [new(Guid.NewGuid(), owner.StaffMemberId, Now.AddMinutes(-20))], [], [], [])
            };
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class WriteBoundary(OperationsDbContext db) : IOperationsDbContext
    {
        public Func<CancellationToken, Task<int>>? Save { get; set; }
        public int SaveCalls { get; private set; }
        public DbSet<Flight> Flights => db.Flights;
        public DbSet<FlightUserActivity> FlightUserActivities => db.FlightUserActivities;
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
