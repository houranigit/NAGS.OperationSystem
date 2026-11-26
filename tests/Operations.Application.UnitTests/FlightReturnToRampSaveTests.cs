using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.Authorization;
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
    public async Task Competing_rtr_open_that_reaches_the_unique_index_returns_stale()
    {
        var interceptor = new SaveCallback();
        await using var f = await Fixture.Create(saveInterceptor: interceptor);
        var winningId = Guid.NewGuid();
        interceptor.BeforeSave = async ct =>
        {
            await using var winnerDb = f.OpenPeerContext();
            var flight = await winnerDb.Flights.Include(item => item.DepartureSubmissions).SingleAsync(ct);
            var receiver = new StaffMemberSnapshot(f.OtherStaff, "Colleague", "E2");
            var arrival = RtrArrivalDetails.Create(Now, receiver, "Competing open").Value;
            flight.SaveReturnToRamp(winningId, arrival, null, [], [], f.Other, Now).IsSuccess.ShouldBeTrue();
            winnerDb.Entry(flight).Property(item => item.RowVersion).CurrentValue = [2, 0, 0, 0, 0, 0, 0, 0];
            await winnerDb.SaveChangesAsync(ct);
            throw new DbUpdateException("Injected competing open occurrence insert failure.");
        };

        var result = await f.Save(Guid.NewGuid(), f.Empty, version: Version);

        result.Error.Code.ShouldBe("General.ConcurrencyConflict");
        var persisted = await f.Db.Flights.AsNoTracking().Include(item => item.ReturnToRamps).SingleAsync();
        var occurrence = persisted.ReturnToRamps.ShouldHaveSingleItem();
        occurrence.Id.ShouldBe(winningId);
        occurrence.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Rtr_open_write_failure_is_rethrown_when_the_flight_version_is_unchanged()
    {
        var interceptor = new SaveCallback();
        await using var f = await Fixture.Create(saveInterceptor: interceptor);
        var failure = new DbUpdateException("Injected unrelated write failure.");
        interceptor.BeforeSave = _ => throw failure;

        var error = await Should.ThrowAsync<DbUpdateException>(() => f.Save(Guid.NewGuid(), f.Empty, version: Version));

        error.ShouldBeSameAs(failure);
        var persisted = await f.Db.Flights.AsNoTracking().Include(item => item.ReturnToRamps).SingleAsync();
        persisted.ReturnToRamps.ShouldBeEmpty();
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
    public async Task Missing_departure_prevents_rtr_for_assigned_and_unassigned_staff(bool assigned)
    {
        await using var f = await Fixture.Create(departure: false, assigned: assigned);
        var result = await f.Save(Guid.NewGuid(), f.Empty);
        result.Error.Code.ShouldBe("Operations.ReturnToRamp.DepartureRequired");
        f.Storage.Files.ShouldBeEmpty(); (await f.Db.Flights.Include(x => x.ReturnToRamps).SingleAsync()).ReturnToRamps.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unassigned_searcher_can_create_start_and_close_rtr_without_gaining_normal_work_rights(bool foreignOccurrence)
    {
        await using var f = await Fixture.Create(assigned: false, approve: false);
        await f.SeedNormalWorkClosedByOther();
        var before = await f.Detail(); before.IsSuccess.ShouldBeTrue();
        AssertUnassignedPermissions(before.Value);
        var id = Guid.NewGuid();
        var created = await f.Save(id, f.Empty, other: foreignOccurrence);
        created.IsSuccess.ShouldBeTrue(); created.Value.ReturnToRamps!.Single().IsOpen.ShouldBeTrue();
        var reloaded = await f.Detail(); reloaded.IsSuccess.ShouldBeTrue();
        reloaded.Value.ReturnToRamps!.Single().CanEdit.ShouldBeTrue(); AssertUnassignedPermissions(reloaded.Value);
        (await f.Save(Guid.NewGuid(), f.Empty)).Error.Code.ShouldBe("Operations.ReturnToRamp.OpenExists");

        var full = f.Full;
        var openWork = full with
        {
            Departure = null,
            Services = full.Services!.Select(s => s with { Input = s.Input with
            {
                EndUtc = null,
                Performers = s.Input.Performers.Select(p => p with { EndUtc = null }).ToArray()
            } }).ToArray(),
            Tasks = full.Tasks!.Select(t => t with { Input = t.Input with
            {
                EndUtc = null,
                Performers = t.Input.Performers.Select(p => p with { EndUtc = null }).ToArray()
            } }).ToArray()
        };
        var started = await f.Save(id, openWork); started.IsSuccess.ShouldBeTrue();
        var active = started.Value.ReturnToRamps!.Single();
        active.IsOpen.ShouldBeTrue(); active.CanEdit.ShouldBeTrue();
        active.Services.Single().EndUtc.ShouldBeNull(); active.Tasks.Single().EndUtc.ShouldBeNull();
        AssertUnassignedPermissions(started.Value);

        var closed = await f.Save(id, full); closed.IsSuccess.ShouldBeTrue();
        var occurrence = closed.Value.ReturnToRamps!.Single();
        occurrence.IsOpen.ShouldBeFalse(); occurrence.HasSignature.ShouldBeTrue();
        occurrence.CreatedByUserId.ShouldBe(foreignOccurrence ? f.Other : f.Owner);
        occurrence.HandledByStaffMemberId.ShouldBe(f.OwnerStaff);
        occurrence.Services.Single().EndUtc.ShouldNotBeNull(); occurrence.Tasks.Single().EndUtc.ShouldNotBeNull();
        AssertUnassignedPermissions(closed.Value);
        var final = await f.Detail(); final.IsSuccess.ShouldBeTrue(); AssertUnassignedPermissions(final.Value);
        final.Value.ReturnToRamps!.Single().IsOpen.ShouldBeFalse();

        var readOnly = await f.Detail(canOperate: false); readOnly.IsSuccess.ShouldBeTrue();
        readOnly.Value.CanSaveReturnsToRamp.ShouldBeFalse(); readOnly.Value.ReturnToRamps!.Single().CanEdit.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unassigned_rtr_authoring_does_not_widen_station_or_time_window(bool differentStation)
    {
        await using var f = await Fixture.Create(assigned: false);
        var scope = new ScopeImpl(new(differentStation ? Guid.NewGuid() : f.Reader.StationId,
            f.OwnerStaff, f.Owner, differentStation ? Now : Now.AddHours(15)));
        var result = await f.Save(Guid.NewGuid(), f.Empty, scopeOverride: scope);
        result.Error.Code.ShouldBe("Operations.Flight.NotFound");
        f.Storage.Files.ShouldBeEmpty(); (await f.Db.Flights.Include(x => x.ReturnToRamps).SingleAsync()).ReturnToRamps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Open_rtr_remains_writable_after_normal_departure_retention_expires()
    {
        await using var f = await Fixture.Create(assigned: false);
        var id = Guid.NewGuid(); (await f.Save(id, f.Empty)).IsSuccess.ShouldBeTrue();
        var laterScope = new ScopeImpl(new(f.Reader.StationId, f.OwnerStaff, f.Owner, Now.AddDays(5)));
        var result = await f.Save(id, f.Full, scopeOverride: laterScope);
        result.IsSuccess.ShouldBeTrue(); result.Value.ReturnToRamps!.Single().IsOpen.ShouldBeFalse();
    }

    private static void AssertUnassignedPermissions(FlightDetailDto detail)
    {
        detail.IsUnassignedSearchFlight.ShouldBeTrue(); detail.CanSaveReturnsToRamp.ShouldBeTrue();
        detail.AssignedEmployees.ShouldNotContain(employee => employee.StaffMemberId == detail.CurrentStaffMemberId);
        detail.CanAddServices.ShouldBe(false); detail.CanAddTasks.ShouldBe(false);
        detail.CanSaveCancellation.ShouldBeFalse(); detail.CanEditFlightDetails.ShouldBeFalse();
        var service = detail.Services!.Single(); var task = detail.Tasks!.Single();
        service.CanEdit.ShouldBeFalse(); service.CanSetEnd.ShouldBeFalse(); service.CanDelete.ShouldBeFalse(); service.CanChangeService.ShouldBeFalse();
        task.CanEdit.ShouldBeFalse(); task.CanSetEnd.ShouldBeFalse(); task.CanDelete.ShouldBeFalse();
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
        private readonly DbContextOptions<OperationsDbContext> peerOptions;
        public OperationsDbContext Db { get; }

        private Fixture(SaveCallback? saveInterceptor)
        {
            var options = new DbContextOptionsBuilder<OperationsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot());
            peerOptions = options.Options;
            if (saveInterceptor is not null)
                options.AddInterceptors(saveInterceptor);
            Db = new OperationsDbContext(options.Options);
        }

        public OperationsDbContext OpenPeerContext() => new(peerOptions);
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
        public async Task<Result<FlightDetailDto>> Save(Guid id, SaveFlightReturnToRampRequest input, bool other = false, byte[]? version = null, IMobileFlightScope? scopeOverride = null)
        {
            Db.ChangeTracker.Clear(); var rowVersion = version ?? (await Db.Flights.SingleAsync()).RowVersion;
            return await new SaveFlightReturnToRampCommandHandler(Db, scopeOverride ?? Scope(other), new(Reader), Reader, Storage, new Clock(), NoopMobileSyncBroadcaster.Instance,
                NoopFlightWorkOrderEmailQueue.Instance,
                new WorkUserNames(new() { [Owner] = "Original", [Other] = "Colleague" })).Handle(new(FlightId, id, input, rowVersion), default);
        }
        public Task<Result<FlightDetailDto>> Detail(bool canOperate = true) =>
            new GetMobileFlightByIdQueryHandler(Db, Scope(false), Reader, new User(Owner, canOperate)).Handle(new(FlightId), default);
        public async Task SeedNormalWorkClosedByOther()
        {
            Db.ChangeTracker.Clear();
            var flight = await Db.Flights.Include(item => item.ArrivalSubmissions)
                .Include(item => item.DepartureSubmissions).SingleAsync();
            var employee = new StaffMemberSnapshot(OwnerStaff, "Original", "E1");
            var start = Now.AddMinutes(-40); var end = Now.AddMinutes(-20);
            var serviceId = Guid.NewGuid(); var service = new ServiceSnapshot(Reader.ServiceId, "Normal service"); var person = Guid.NewGuid();
            flight.SaveService(serviceId, service, null, TimeWindow.Create(start, null).Value, null,
                [new(employee, TimeWindow.Create(start, null).Value, person)], Owner, Now).IsSuccess.ShouldBeTrue();
            flight.SaveService(serviceId, service, null, TimeWindow.Create(start, end).Value, null,
                [new(employee, TimeWindow.Create(start, end).Value, person)], Other, Now).IsSuccess.ShouldBeTrue();
            var taskId = Guid.NewGuid(); var taskPerson = Guid.NewGuid();
            flight.SaveTask(taskId, (FlightTaskType)0, null, null, TimeWindow.Create(start, null).Value, "Normal task",
                [new(employee, TimeWindow.Create(start, null).Value, null, taskPerson)], [], [], [], Owner, Now).IsSuccess.ShouldBeTrue();
            flight.SaveTask(taskId, (FlightTaskType)0, null, null, TimeWindow.Create(start, end).Value, "Normal task",
                [new(employee, TimeWindow.Create(start, end).Value, null, taskPerson)], [], [], [], Other, Now).IsSuccess.ShouldBeTrue();
            flight.Approve(Other, "Colleague", Now, 1).IsSuccess.ShouldBeTrue();
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
        }
        public async Task ChangeVersion()
        {
            Db.ChangeTracker.Clear(); var flight = await Db.Flights.SingleAsync(); Db.Entry(flight).Property(x => x.RowVersion).CurrentValue = [2, 0, 0, 0, 0, 0, 0, 0]; await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
        }
        public static async Task<Fixture> Create(bool departure = true, bool assigned = true, SaveCallback? saveInterceptor = null, bool approve = true)
        {
            var f = new Fixture(saveInterceptor);
            f.Reader.StaffMembers[f.OwnerStaff] = new(f.OwnerStaff, "Original", "E1", f.Reader.StationId, f.Owner, true);
            f.Reader.StaffMembers[f.OtherStaff] = new(f.OtherStaff, "Colleague", "E2", f.Reader.StationId, f.Other, true);
            var owner = new StaffMemberSnapshot(f.OwnerStaff, "Original", "E1"); var other = new StaffMemberSnapshot(f.OtherStaff, "Colleague", "E2");
            var flight = Flight.ScheduleNew(new(Guid.NewGuid(), "SV", "Customer"), new(f.Reader.StationId, "RUH", "Riyadh"), new(Guid.NewGuid(), "Transit"),
                FlightNumber.Create("SV100", "SV").Value, ScheduledTime.Create(Now.AddHours(-2), Now.AddHours(2)).Value, null, [new(f.Reader.ServiceId, "Planned service")], assigned ? [owner, other] : [other], f.Owner, Now).Value;
            flight.SubmitArrival(owner, ArrivalDetails.Create(null, Now.AddHours(-1), new(Guid.NewGuid(), "Airbus", "A320"), "HZ-TEST", owner, null).Value, f.Owner, Now);
            if (departure)
            {
                flight.SubmitDeparture(owner, DepartureDetails.Create(Now.AddMinutes(-10), null, owner, null).Value, f.Owner, Now).IsSuccess.ShouldBeTrue();
                if (approve) flight.Approve(f.Owner, "Original", Now, 1).IsSuccess.ShouldBeTrue();
            }
            f.Db.Flights.Add(flight); f.Db.Entry(flight).Property(x => x.RowVersion).CurrentValue = Version.ToArray(); await f.Db.SaveChangesAsync(); f.FlightId = flight.Id; f.Db.ChangeTracker.Clear(); return f;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class SaveCallback : SaveChangesInterceptor
    {
        public Func<CancellationToken, Task>? BeforeSave { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeSave is { } callback)
                await callback(cancellationToken);
            return result;
        }
    }

    private sealed class ScopeImpl(MobileFlightScopeContext value) : IMobileFlightScope { public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken ct) => Task.FromResult(Result.Success(value)); }
    private sealed class User(Guid id, bool canOperate) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => id;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.StationStaff;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => canOperate && permission == OperationsPermissions.Flights.Operate;
    }
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
