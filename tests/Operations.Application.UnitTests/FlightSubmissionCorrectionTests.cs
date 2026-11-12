using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightSubmissionCorrectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Version = [1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Assigned_correction_keeps_original_attribution_and_queues_one_event_in_same_save(bool departure, bool target)
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.CorrectAsync(departure, target: target);

        result.IsSuccess.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(1);
        fixture.Writes.EventCountAtSave.ShouldBe(1);
        var updated = await fixture.ReadAsync();
        var original = departure ? updated.DepartureSubmissions.Single().SubmittedByEmployee : updated.ArrivalSubmissions.Single().SubmittedByEmployee;
        original.ShouldBe(fixture.Author);
        if (departure)
        {
            var item = updated.DepartureSubmissions.Single();
            item.Id.ShouldBe(fixture.DepartureId);
            item.CreatedByUserId.ShouldBe(fixture.AuthorUserId);
            item.CreatedAtUtc.ShouldBe(Now.AddMinutes(-5));
            item.UpdatedByUserId.ShouldBe(fixture.Actor.UserId);
            item.UpdatedAtUtc.ShouldBe(Now);
            item.IsAccepted.ShouldBeTrue();
            item.Details.HandledByEmployee.ShouldBe(fixture.Receiver);
            item.Details.CustomerSignature.StorageKey.ShouldBe(Fixture.OriginalKey);
            fixture.Storage.Files.ShouldHaveSingleItem();
            fixture.Storage.SaveCalls.ShouldBe(0);
        }
        else
        {
            var item = updated.ArrivalSubmissions.Single();
            item.Id.ShouldBe(fixture.ArrivalId);
            item.CreatedByUserId.ShouldBe(fixture.AuthorUserId);
            item.CreatedAtUtc.ShouldBe(Now.AddMinutes(-10));
            item.UpdatedByUserId.ShouldBe(fixture.Actor.UserId);
            item.UpdatedAtUtc.ShouldBe(Now);
            item.IsAccepted.ShouldBeTrue();
            item.Details.ReceivedByEmployee.ShouldBe(fixture.Receiver);
        }
        var message = await fixture.Db.OutboxMessages.SingleAsync();
        var correction = JsonSerializer.Deserialize<FlightSubmissionUpdated>(message.Content)!;
        correction.EventId.ShouldBe(message.Id);
        correction.SubmissionId.ShouldBe(departure ? fixture.DepartureId : fixture.ArrivalId);
        correction.SubmissionKind.ShouldBe(departure ? FlightSubmissionKind.Departure : FlightSubmissionKind.Arrival);
        correction.SubmittedByUserId.ShouldBe(fixture.AuthorUserId);
        correction.SubmittedByStaffMemberId.ShouldBe(fixture.Author.StaffMemberId);
        correction.UpdatedByUserId.ShouldBe(fixture.Actor.UserId);
        correction.UpdatedByStaffMemberId.ShouldBe(fixture.Actor.StaffMemberId);
        correction.UpdatedByDisplayName.ShouldBe("Assigned editor");
        message.Content.ShouldNotContain(Fixture.OriginalKey);
        message.Content.ShouldNotContain("Corrected remarks");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unchanged_foreign_input_is_acknowledged_without_a_write_or_event(bool departure, bool target)
    {
        await using var fixture = await Fixture.CreateAsync();

        (await fixture.CorrectAsync(departure, target: target, remarks: departure ? "Departure" : "Arrival")).IsSuccess.ShouldBeTrue();

        fixture.Writes.SaveCalls.ShouldBe(0);
        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(0);
        var persisted = await fixture.ReadAsync();
        persisted.ArrivalSubmissions.Single().UpdatedAtUtc.ShouldBeNull();
        persisted.DepartureSubmissions.Single().UpdatedAtUtc.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Original_submitter_own_correction_does_not_send_a_colleague_notification(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync();

        (await fixture.CorrectAsync(departure, target: false, asAuthor: true)).IsSuccess.ShouldBeTrue();

        fixture.Writes.SaveCalls.ShouldBe(1);
        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(0);
        var persisted = await fixture.ReadAsync();
        (departure ? persisted.DepartureSubmissions.Single().UpdatedByUserId : persisted.ArrivalSubmissions.Single().UpdatedByUserId)
            .ShouldBe(fixture.AuthorUserId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Exact_retry_with_stale_version_does_not_write_or_queue_another_notification(bool departure, bool target)
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.CorrectAsync(departure, target: target)).IsSuccess.ShouldBeTrue();
        fixture.Db.ChangeTracker.Clear();

        var replay = await fixture.CorrectAsync(departure, target: target, version: [9, 0, 0, 0, 0, 0, 0, 0]);

        replay.IsSuccess.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(1);
        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ambiguous_commit_retry_uses_same_submission_and_event(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Writes.Save = async ct =>
        {
            await fixture.Db.SaveChangesAsync(ct);
            throw new IOException("Injected response loss after commit");
        };
        await Should.ThrowAsync<IOException>(() => fixture.CorrectAsync(departure));
        fixture.Db.ChangeTracker.Clear();

        (await fixture.CorrectAsync(departure)).IsSuccess.ShouldBeTrue();

        fixture.Writes.SaveCalls.ShouldBe(1);
        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(1);
        fixture.Storage.Files.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unassigned_per_landing_reader_cannot_correct_foreign_submission_or_read_nonvisible_signature(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync(assigned: false, anotherDeparture: true);

        var result = await fixture.CorrectAsync(departure);

        result.Error.Code.ShouldBe(departure ? "Operations.DepartureSubmission.AssignmentRequired" : "Operations.ArrivalSubmission.AssignmentRequired");
        fixture.Writes.SaveCalls.ShouldBe(0);
        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(0);
        (await fixture.SignatureAsync(fixture.OtherDepartureId!.Value, Now.AddMinutes(-4))).Error.Code
            .ShouldBe("Operations.Flight.DepartureSignatureNotFound");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Untargeted_peer_correction_requires_assignment(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync(assigned: false);

        var result = await fixture.CorrectAsync(departure, target: false);

        result.Error.Code.ShouldBe(departure ? "Operations.DepartureSubmission.AssignmentRequired" : "Operations.ArrivalSubmission.AssignmentRequired");
        fixture.Writes.SaveCalls.ShouldBe(0);
        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multiple_peer_submissions_require_a_target_instead_of_selecting_one_arbitrarily(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync(anotherDeparture: true, anotherArrival: true);

        var result = await fixture.CorrectAsync(departure, target: false);

        result.Error.Code.ShouldBe(departure ? "Operations.DepartureSubmission.TargetRequired" : "Operations.ArrivalSubmission.TargetRequired");
        fixture.Writes.SaveCalls.ShouldBe(0);
        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_changed_input_and_unknown_target_cannot_write_or_notify(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync();

        (await fixture.CorrectAsync(departure, version: [9, 0, 0, 0, 0, 0, 0, 0])).Error.Code.ShouldBe("General.ConcurrencyConflict");
        (await fixture.CorrectAsync(departure, targetId: Guid.NewGuid())).Error.Code
            .ShouldBe(departure ? "Operations.DepartureSubmission.NotFound" : "Operations.ArrivalSubmission.NotFound");

        fixture.Writes.SaveCalls.ShouldBe(0);
        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_save_does_not_persist_correction_or_notification(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Writes.Save = _ => throw new DbUpdateConcurrencyException("Injected race before commit");

        (await fixture.CorrectAsync(departure)).Error.Code.ShouldBe("General.ConcurrencyConflict");
        fixture.Db.ChangeTracker.Clear();

        (await fixture.Db.OutboxMessages.CountAsync()).ShouldBe(0);
        var persisted = await fixture.ReadAsync();
        persisted.ArrivalSubmissions.Single().Details.Remarks.ShouldBe("Arrival");
        persisted.DepartureSubmissions.Single().Details.Remarks.ShouldBe("Departure");
    }

    [Fact]
    public async Task Assigned_exact_target_signature_is_private_versioned_and_not_limited_to_visible_departure()
    {
        await using var fixture = await Fixture.CreateAsync(anotherDeparture: true);

        var signature = await fixture.SignatureAsync(fixture.OtherDepartureId!.Value, Now.AddMinutes(-4));

        signature.IsSuccess.ShouldBeTrue();
        await using var content = signature.Value.Content;
        using var bytes = new MemoryStream();
        await content.CopyToAsync(bytes);
        bytes.ToArray().ShouldBe(Png);
        (await fixture.SignatureAsync(fixture.OtherDepartureId.Value, Now)).Error.Code
            .ShouldBe("Operations.Flight.DepartureSignatureChanged");
        (await fixture.SignatureAsync(Guid.NewGuid(), Now)).Error.Code
            .ShouldBe("Operations.Flight.DepartureSignatureNotFound");
    }

    [Fact]
    public async Task Replacement_signature_is_stored_without_reassigning_submission_or_leaking_storage_reference()
    {
        await using var fixture = await Fixture.CreateAsync();
        var input = new SaveFlightDepartureRequest(Now, Png, Now, "Corrected remarks", TargetSubmissionId: fixture.DepartureId);

        var result = await fixture.Departures.Handle(new(fixture.FlightId, input, Version), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Submission.SubmittedByEmployee.StaffMemberId.ShouldBe(fixture.Author.StaffMemberId);
        fixture.Storage.SaveCalls.ShouldBe(1);
        fixture.Storage.Files.ShouldHaveSingleItem();
        fixture.Storage.Files.ShouldNotContainKey(Fixture.OriginalKey);
        var message = await fixture.Db.OutboxMessages.SingleAsync();
        message.Content.ShouldNotContain(".png");
        (await fixture.ReadAsync()).DepartureSubmissions.Single().Details.CustomerSignature.SignedAtUtc.ShouldBe(Now);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string OriginalKey = "private-signatures/original.png";
        public required OperationsDbContext Db { get; init; }
        public required WriteBoundary Writes { get; init; }
        public required MemoryStorage Storage { get; init; }
        public required MobileFlightScopeContext Actor { get; init; }
        public required StaffMemberSnapshot Author { get; init; }
        public required StaffMemberSnapshot Receiver { get; init; }
        public required Guid AuthorUserId { get; init; }
        public required Guid FlightId { get; init; }
        public required Guid AircraftId { get; init; }
        public required Guid ArrivalId { get; init; }
        public required Guid DepartureId { get; init; }
        public Guid? OtherDepartureId { get; init; }
        public required SaveFlightArrivalCommandHandler Arrivals { get; init; }
        public required SaveFlightDepartureCommandHandler Departures { get; init; }
        public required SaveFlightArrivalCommandHandler AuthorArrivals { get; init; }
        public required SaveFlightDepartureCommandHandler AuthorDepartures { get; init; }

        public async Task<Result> CorrectAsync(bool departure, bool target = true, Guid? targetId = null, byte[]? version = null,
            string remarks = "Corrected remarks", bool asAuthor = false)
        {
            if (departure)
            {
                var result = await (asAuthor ? AuthorDepartures : Departures).Handle(new(FlightId,
                    new(Now, null, null, remarks, TargetSubmissionId: target ? targetId ?? DepartureId : null), version ?? Version), CancellationToken.None);
                return result.IsSuccess ? Result.Success() : result.Error;
            }
            var arrival = await (asAuthor ? AuthorArrivals : Arrivals).Handle(new(FlightId,
                new(Now.AddHours(-1), AircraftId, "HZ-TEST", "REF", remarks, TargetSubmissionId: target ? targetId ?? ArrivalId : null), version ?? Version), CancellationToken.None);
            return arrival.IsSuccess ? Result.Success() : arrival.Error;
        }

        public Task<Result<FlightDepartureSignatureContent>> SignatureAsync(Guid id, DateTimeOffset expected) =>
            new GetMobileFlightDepartureSignatureQueryHandler(Db, new Scope(Actor), Storage)
                .Handle(new(FlightId, id, expected), CancellationToken.None);

        public async Task<Flight> ReadAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Flights.Include(f => f.ArrivalSubmissions).Include(f => f.DepartureSubmissions).SingleAsync();
        }

        public static async Task<Fixture> CreateAsync(bool assigned = true, bool anotherDeparture = false, bool anotherArrival = false)
        {
            var db = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var reader = new SchedulingMasterDataReader();
            var actor = new MobileFlightScopeContext(reader.StationId, Guid.NewGuid(), Guid.NewGuid(), Now);
            var editor = new StaffMemberSnapshot(actor.StaffMemberId, "Assigned editor", "E2");
            reader.StaffMembers[editor.StaffMemberId] = new(editor.StaffMemberId, editor.FullName, editor.EmployeeId, reader.StationId, actor.UserId, true);
            var aircraft = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
            reader.AircraftTypes[aircraft.AircraftTypeId] = new(aircraft.AircraftTypeId, aircraft.Manufacturer, aircraft.Model, true);
            var author = new StaffMemberSnapshot(Guid.NewGuid(), "Original submitter", "E1");
            var receiver = new StaffMemberSnapshot(Guid.NewGuid(), "Original receiving and handling employee", "E3");
            var authorUserId = Guid.NewGuid();
            var authorActor = new MobileFlightScopeContext(reader.StationId, author.StaffMemberId, authorUserId, Now);
            reader.StaffMembers[author.StaffMemberId] = new(author.StaffMemberId, author.FullName, author.EmployeeId, reader.StationId, authorUserId, true);
            var flight = Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Customer"),
                new StationSnapshot(reader.StationId, "RUH", "Riyadh"), new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
                FlightNumber.Create("SV100", "SV").Value, ScheduledTime.Create(Now.AddHours(-1), Now.AddHours(1)).Value,
                aircraft, [new ServiceSnapshot(assigned ? reader.ServiceId : WellKnownMasterDataIds.AircraftPerLandingService, "Service")],
                assigned ? [editor, author] : [], authorUserId, Now.AddHours(-2)).Value;
            var arrival = flight.SubmitArrival(author, ArrivalDetails.Create("REF", Now.AddHours(-1), aircraft, "HZ-TEST", receiver, "Arrival").Value,
                authorUserId, Now.AddMinutes(-10)).Value;
            if (anotherArrival)
                flight.SubmitArrival(new(Guid.NewGuid(), "Another submitter", "E4"),
                    ArrivalDetails.Create("REF", Now.AddHours(-1), aircraft, "HZ-OTHER", receiver, "Another arrival").Value,
                    Guid.NewGuid(), Now.AddMinutes(-9)).IsSuccess.ShouldBeTrue();
            var signature = CustomerSignature.Create("customer.png", OriginalKey, "image/png", Png.Length, Now.AddMinutes(-6)).Value;
            var departure = flight.SubmitDeparture(author, DepartureDetails.Create(Now, signature, receiver, "Departure").Value,
                authorUserId, Now.AddMinutes(-5)).Value;
            Guid? otherDepartureId = null;
            if (anotherDeparture)
                otherDepartureId = flight.SubmitDeparture(new(Guid.NewGuid(), "Another submitter", "E4"),
                    DepartureDetails.Create(Now, signature, receiver, "Another departure").Value, Guid.NewGuid(), Now.AddMinutes(-4)).Value.Id;
            db.Flights.Add(flight);
            db.Entry(flight).Property(f => f.RowVersion).CurrentValue = Version.ToArray();
            await db.SaveChangesAsync();
            var writes = new WriteBoundary(db);
            var storage = new MemoryStorage();
            storage.Files[OriginalKey] = Png;
            return new Fixture { Db = db, Writes = writes, Storage = storage, Actor = actor, Author = author, Receiver = receiver,
                AuthorUserId = authorUserId, FlightId = flight.Id, AircraftId = aircraft.AircraftTypeId, ArrivalId = arrival.Id, DepartureId = departure.Id,
                OtherDepartureId = otherDepartureId,
                Arrivals = new(writes, new Scope(actor), new MasterDataResolver(reader), new Clock(), NoopMobileSyncBroadcaster.Instance),
                Departures = new(writes, new Scope(actor), new MasterDataResolver(reader), new Clock(), NoopMobileSyncBroadcaster.Instance, storage),
                AuthorArrivals = new(writes, new Scope(authorActor), new MasterDataResolver(reader), new Clock(), NoopMobileSyncBroadcaster.Instance),
                AuthorDepartures = new(writes, new Scope(authorActor), new MasterDataResolver(reader), new Clock(), NoopMobileSyncBroadcaster.Instance, storage) };
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class WriteBoundary(OperationsDbContext db) : IOperationsDbContext
    {
        public int SaveCalls { get; private set; }
        public int EventCountAtSave { get; private set; }
        public Func<CancellationToken, Task<int>>? Save { get; set; }
        public DbSet<Flight> Flights => db.Flights;
        public DbSet<OutboxMessage> OutboxMessages => db.OutboxMessages;
        public DbSet<InboxMessage> InboxMessages => db.InboxMessages;
        public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class => db.SetOriginalRowVersion(entity, rowVersion);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            EventCountAtSave = db.ChangeTracker.Entries<OutboxMessage>().Count(entry => entry.State == EntityState.Added);
            return Save?.Invoke(cancellationToken) ?? db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public int SaveCalls { get; private set; }
        public async Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var key = $"{container}/{Guid.NewGuid():N}.png";
            Files[key] = buffer.ToArray();
            return new(key, contentType, buffer.Length);
        }
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }
    private sealed class Scope(MobileFlightScopeContext context) : IMobileFlightScope
    {
        public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(context));
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
