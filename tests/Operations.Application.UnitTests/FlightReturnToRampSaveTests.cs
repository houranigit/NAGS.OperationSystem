using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
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

public sealed class FlightReturnToRampSaveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Version = [1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");

    [Fact]
    public async Task Open_arrival_saves_as_a_real_occurrence_and_next_requires_its_closure()
    {
        await using var f = await Fixture.Create(); var id = Guid.NewGuid();
        var result = await f.Save(id, f.Empty);
        result.IsSuccess.ShouldBeTrue(); result.Value.ReturnToRamps!.Single().Id.ShouldBe(id);
        result.Value.ReturnToRamps!.Single().IsOpen.ShouldBeTrue(); result.Value.CanSaveReturnsToRamp.ShouldBeTrue();
        (await f.Save(Guid.NewGuid(), f.Empty)).Error.Code.ShouldBe("Operations.ReturnToRamp.OpenExists");
        (await f.Db.FlightUserActivities.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Full_graph_persists_media_attribution_and_idempotent_foreign_correction_notification()
    {
        await using var f = await Fixture.Create(); var id = Guid.NewGuid(); var input = f.Full;
        var saved = await f.Save(id, input); saved.IsSuccess.ShouldBeTrue();
        var rtr = saved.Value.ReturnToRamps!.Single(); rtr.IsOpen.ShouldBeFalse(); rtr.HasSignature.ShouldBeTrue();
        rtr.Services.Single().Attachments.ShouldHaveSingleItem(); rtr.Tasks.ShouldHaveSingleItem(); f.Storage.Files.Count.ShouldBe(2);
        var correction = input with { Arrival = input.Arrival with { Remarks = "Corrected arrival" } };
        var updated = await f.Save(id, correction, other: true); updated.IsSuccess.ShouldBeTrue();
        var current = updated.Value.ReturnToRamps!.Single(); current.CreatedByUserId.ShouldBe(f.Owner); current.UpdatedByUserId.ShouldBe(f.Other);
        current.Services.Single().CreatedByUserId.ShouldBe(f.Owner); current.Services.Single().Performers.Single().Id.ShouldBe(input.Services!.Single().Input.Performers.Single().Id);
        var notice = JsonSerializer.Deserialize<FlightSubmissionUpdated>((await f.Db.OutboxMessages.SingleAsync()).Content)!;
        notice.SubmissionKind.ShouldBe(FlightSubmissionKind.ReturnToRamp); notice.SubmittedByUserId.ShouldBe(f.Owner); notice.UpdatedByUserId.ShouldBe(f.Other);
        await f.ChangeVersion();
        var replay = await f.Save(id, correction, true, Version); replay.IsSuccess.ShouldBeTrue();
        f.Storage.Files.Count.ShouldBe(2); (await f.Db.OutboxMessages.CountAsync()).ShouldBe(1);
        (await f.Save(id, correction with { Arrival = correction.Arrival with { Remarks = "Stale" } }, true, Version)).Error.Code.ShouldBe("General.ConcurrencyConflict");
    }

    [Fact]
    public async Task Failed_closure_or_stale_version_publish_no_partial_work_or_new_media()
    {
        await using var f = await Fixture.Create(); var id = Guid.NewGuid();
        (await f.Save(id, f.Empty)).IsSuccess.ShouldBeTrue();
        var invalid = f.Full with { Departure = f.Full.Departure! with { DepartedAtUtc = Now.AddMinutes(8) } };
        (await f.Save(id, invalid)).IsFailure.ShouldBeTrue();
        f.Db.ChangeTracker.Clear(); var stored = await f.Db.Flights.Include(x => x.ReturnToRamps).Include(x => x.ServiceRecords).SingleAsync();
        stored.ReturnToRamps.Single().IsOpen.ShouldBeTrue(); stored.ServiceRecords.ShouldBeEmpty(); f.Storage.Files.ShouldBeEmpty();
        await f.ChangeVersion();
        (await f.Save(id, f.Full, version: Version)).Error.Code.ShouldBe("General.ConcurrencyConflict"); f.Storage.Files.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_departure_or_unassigned_regular_search_cannot_create_rtr(bool noDeparture)
    {
        await using var f = await Fixture.Create(departure: !noDeparture, assigned: noDeparture);
        var result = await f.Save(Guid.NewGuid(), f.Empty);
        result.Error.Code.ShouldBe(noDeparture ? "Operations.ReturnToRamp.DepartureRequired" : "Operations.ReturnToRamp.AssignmentRequired");
        f.Storage.Files.ShouldBeEmpty(); (await f.Db.Flights.Include(x => x.ReturnToRamps).SingleAsync()).ReturnToRamps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rtr_media_routes_read_stored_content_and_reject_changed_preview_versions()
    {
        await using var f = await Fixture.Create(); var id = Guid.NewGuid(); var saved = await f.Save(id, f.Full); saved.IsSuccess.ShouldBeTrue();
        var r = saved.Value.ReturnToRamps!.Single();
        var signatureHandler = new GetMobileFlightReturnToRampSignatureQueryHandler(f.Db, f.Scope(false), f.Storage);
        (await signatureHandler.Handle(new(f.FlightId, id, (r.UpdatedAtUtc ?? r.CreatedAtUtc).AddSeconds(1)), default)).Error.Code.ShouldBe("Operations.ReturnToRamp.SignatureChanged");
        var signature = await signatureHandler.Handle(new(f.FlightId, id, r.UpdatedAtUtc ?? r.CreatedAtUtc), default); signature.IsSuccess.ShouldBeTrue();
        using var copy = new MemoryStream(); await signature.Value.Content.CopyToAsync(copy); copy.ToArray().ShouldBe(Png);
        var service = r.Services.Single(); var attachment = service.Attachments.Single();
        var image = await new GetMobileFlightServiceAttachmentQueryHandler(f.Db, f.Scope(false), f.Storage).Handle(new(f.FlightId, service.Id, attachment.Id, attachment.UploadedAtUtc), default);
        image.IsSuccess.ShouldBeTrue(); image.Value.ContentType.ShouldBe("image/png");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required OperationsDbContext Db { get; init; }
        public SchedulingMasterDataReader Reader { get; } = new();
        public MemoryStorage Storage { get; } = new();
        public Guid Owner { get; } = Guid.NewGuid(); public Guid Other { get; } = Guid.NewGuid();
        public Guid OwnerStaff { get; } = Guid.NewGuid(); public Guid OtherStaff { get; } = Guid.NewGuid();
        public Guid FlightId { get; private set; }
        public SaveFlightReturnToRampRequest Empty => new(new(Now, "Arrival"), null, [], []);
        private readonly Guid _service = Guid.NewGuid(), _task = Guid.NewGuid(), _performer = Guid.NewGuid(), _taskPerformer = Guid.NewGuid(), _image = Guid.NewGuid();
        public SaveFlightReturnToRampRequest Full => new(new(Now, "Arrival"), new(Now.AddMinutes(30), "Departure", Png, "signature.png", "image/png", Now),
            [new(_service, new(Reader.ServiceId, Now.AddMinutes(5), Now.AddMinutes(15), "WP-1", "Service", [new(_performer, OwnerStaff, Now.AddMinutes(5), Now.AddMinutes(15))], [new(_image, 0, "photo.png", "image/png", Png)]))],
            [new(_task, new(0, null, Now.AddMinutes(10), Now.AddMinutes(20), null, "Task", [new(_taskPerformer, OwnerStaff, Now.AddMinutes(10), Now.AddMinutes(20))], [], [], []))]);
        public IMobileFlightScope Scope(bool other) => new ScopeImpl(new(Reader.StationId, other ? OtherStaff : OwnerStaff, other ? Other : Owner, Now));
        public async Task<Result<FlightDetailDto>> Save(Guid id, SaveFlightReturnToRampRequest input, bool other = false, byte[]? version = null)
        {
            Db.ChangeTracker.Clear(); var rowVersion = version ?? (await Db.Flights.SingleAsync()).RowVersion;
            return await new SaveFlightReturnToRampCommandHandler(Db, Scope(other), new(Reader), Reader, Storage, new Clock(), NoopMobileSyncBroadcaster.Instance,
                new WorkUserNames(new() { [Owner] = "Original", [Other] = "Colleague" })).Handle(new(FlightId, id, input, rowVersion), default);
        }
        public async Task ChangeVersion()
        {
            Db.ChangeTracker.Clear(); var flight = await Db.Flights.SingleAsync(); Db.Entry(flight).Property(x => x.RowVersion).CurrentValue = [2, 0, 0, 0, 0, 0, 0, 0]; await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
        }
        public static async Task<Fixture> Create(bool departure = true, bool assigned = true)
        {
            var f = new Fixture { Db = new(new DbContextOptionsBuilder<OperationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options) };
            f.Reader.StaffMembers[f.OwnerStaff] = new(f.OwnerStaff, "Original", "E1", f.Reader.StationId, f.Owner, true);
            f.Reader.StaffMembers[f.OtherStaff] = new(f.OtherStaff, "Colleague", "E2", f.Reader.StationId, f.Other, true);
            var owner = new StaffMemberSnapshot(f.OwnerStaff, "Original", "E1"); var other = new StaffMemberSnapshot(f.OtherStaff, "Colleague", "E2");
            var flight = Flight.ScheduleNew(new(Guid.NewGuid(), "SV", "Customer"), new(f.Reader.StationId, "RUH", "Riyadh"), new(Guid.NewGuid(), "Transit"),
                FlightNumber.Create("SV100", "SV").Value, ScheduledTime.Create(Now.AddHours(-2), Now.AddHours(2)).Value, null, [new(f.Reader.ServiceId, "Planned service")], assigned ? [owner, other] : [other], f.Owner, Now).Value;
            flight.SubmitArrival(owner, ArrivalDetails.Create(null, Now.AddHours(-1), new(Guid.NewGuid(), "Airbus", "A320"), "HZ-TEST", owner, null).Value, f.Owner, Now);
            if (departure) flight.SubmitDeparture(owner, DepartureDetails.Create(Now.AddMinutes(-10), null, owner, null).Value, f.Owner, Now);
            f.Db.Flights.Add(flight); f.Db.Entry(flight).Property(x => x.RowVersion).CurrentValue = Version.ToArray(); await f.Db.SaveChangesAsync(); f.FlightId = flight.Id; f.Db.ChangeTracker.Clear(); return f;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class ScopeImpl(MobileFlightScopeContext value) : IMobileFlightScope { public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken ct) => Task.FromResult(Result.Success(value)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class MemoryStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public async Task<StoredFile> SaveAsync(string container, string name, string type, Stream content, CancellationToken ct = default)
        { using var copy = new MemoryStream(); await content.CopyToAsync(copy, ct); var key = container + "/" + Guid.NewGuid(); Files[key] = copy.ToArray(); return new(key, type, copy.Length); }
        public Task<Stream?> OpenAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);
        public Task DeleteAsync(string key, CancellationToken ct = default) { Files.Remove(key); return Task.CompletedTask; }
    }
}
