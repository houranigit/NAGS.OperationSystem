using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
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

public sealed class FlightActualServiceBoundsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] InitialVersion = [1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_or_changed_ata_cannot_move_after_retained_service_or_employee_starts(bool existingArrival)
    {
        await using var fixture = await Fixture.CreateAsync(existingArrival: existingArrival);

        var result = await fixture.SaveArrivalAsync(Now.AddMinutes(15));

        result.IsFailure.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(0);
        var retained = await fixture.ReadAsync();
        if (existingArrival) retained.ArrivalSubmissions.ShouldHaveSingleItem().Details.Ata.ShouldBe(fixture.InitialArrival);
        else retained.ArrivalSubmissions.ShouldBeEmpty();
        retained.Services.ShouldHaveSingleItem().Window.FromUtc.ShouldBe(Now);
        retained.Services.Single().Performers.ShouldHaveSingleItem().Window.FromUtc.ShouldBe(Now.AddMinutes(10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_or_changed_atd_cannot_move_before_a_retained_closed_service_end(bool existingDeparture)
    {
        await using var fixture = await Fixture.CreateAsync(existingDeparture: existingDeparture);

        var result = await fixture.SaveDepartureAsync(Now.AddMinutes(25));

        result.IsFailure.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
        var retained = await fixture.ReadAsync();
        if (existingDeparture) retained.DepartureSubmissions.ShouldHaveSingleItem().Details.Atd.ShouldBe(fixture.InitialDeparture);
        else retained.DepartureSubmissions.ShouldBeEmpty();
        retained.Services.ShouldHaveSingleItem().Window.ToUtc.ShouldBe(Now.AddMinutes(30));
    }

    [Theory]
    [InlineData("service-start")]
    [InlineData("employee-start")]
    [InlineData("employee-end")]
    public async Task At_first_or_corrected_departure_open_work_starts_and_employee_ends_remain_bounded(string target)
    {
        await using var fixture = await Fixture.CreateAsync(closedService: false, closedPerformer: target == "employee-end");
        var atd = target switch
        {
            "service-start" => Now.AddMinutes(-1),
            "employee-start" => Now.AddMinutes(5),
            _ => Now.AddMinutes(15)
        };

        var result = await fixture.SaveDepartureAsync(atd);

        result.IsFailure.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
        var retained = await fixture.ReadAsync();
        retained.DepartureSubmissions.ShouldHaveSingleItem().Details.Atd.ShouldBe(fixture.InitialDeparture);
        retained.Services.ShouldHaveSingleItem().Window.ToUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Valid_actual_time_corrections_allow_equality_with_retained_work_bounds()
    {
        await using var fixture = await Fixture.CreateAsync();

        var arrival = await fixture.SaveArrivalAsync(Now);
        var departure = await fixture.SaveDepartureAsync(Now.AddMinutes(30));

        arrival.IsSuccess.ShouldBeTrue();
        departure.IsSuccess.ShouldBeTrue();
        arrival.Value.Submission.Ata.ShouldBe(Now);
        departure.Value.Submission.Atd.ShouldBe(Now.AddMinutes(30));
        var retained = await fixture.ReadAsync();
        retained.Services.ShouldHaveSingleItem().Window.FromUtc.ShouldBe(Now);
        retained.Services.Single().Window.ToUtc.ShouldBe(Now.AddMinutes(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unchanged_actual_time_allows_metadata_corrections_even_when_legacy_work_does_not_fit(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync(
            arrival: departure ? null : Now.AddMinutes(15),
            departure: departure ? Now.AddMinutes(15) : null);

        if (departure)
        {
            var result = await fixture.SaveDepartureAsync(fixture.InitialDeparture, remarks: "Corrected departure remarks");
            result.IsSuccess.ShouldBeTrue();
            result.Value.Submission.Remarks.ShouldBe("Corrected departure remarks");
            result.Value.Submission.Atd.ShouldBe(fixture.InitialDeparture);
        }
        else
        {
            var result = await fixture.SaveArrivalAsync(fixture.InitialArrival, tail: "HZ-CORRECTED");
            result.IsSuccess.ShouldBeTrue();
            result.Value.Submission.AircraftTailNumber.ShouldBe("HZ-CORRECTED");
            result.Value.Submission.Ata.ShouldBe(fixture.InitialArrival);
        }
        fixture.Writes.SaveCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_old_version_replay_acknowledges_saved_actual_input_before_retained_work_validation(bool departure)
    {
        await using var fixture = await Fixture.CreateAsync(
            arrival: departure ? null : Now.AddMinutes(15),
            departure: departure ? Now.AddMinutes(15) : null);
        await fixture.ChangeVersionAsync();

        if (departure)
        {
            var result = await fixture.SaveDepartureAsync(fixture.InitialDeparture, version: InitialVersion);
            result.IsSuccess.ShouldBeTrue();
            result.Value.RowVersion.ShouldNotBe(Convert.ToBase64String(InitialVersion));
        }
        else
        {
            var result = await fixture.SaveArrivalAsync(fixture.InitialArrival, version: InitialVersion);
            result.IsSuccess.ShouldBeTrue();
            result.Value.RowVersion.ShouldNotBe(Convert.ToBase64String(InitialVersion));
        }
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_normal_work_or_only_rtr_work_does_not_restrict_normal_actual_corrections(bool rtrOnly)
    {
        await using (var arrivalFixture = await Fixture.CreateAsync(work: rtrOnly ? Work.Rtr : Work.None))
        {
            var result = await arrivalFixture.SaveArrivalAsync(rtrOnly ? Now.AddMinutes(140) : Now.AddMinutes(1));

            result.IsSuccess.ShouldBeTrue();
            (await arrivalFixture.ReadAsync()).Services.ShouldBeEmpty();
        }
        await using (var departureFixture = await Fixture.CreateAsync(work: rtrOnly ? Work.Rtr : Work.None))
        {
            var result = await departureFixture.SaveDepartureAsync(rtrOnly ? Now.AddMinutes(130) : Now.AddMinutes(-1));

            result.IsSuccess.ShouldBeTrue();
            var retained = await departureFixture.ReadAsync();
            retained.Services.ShouldBeEmpty();
            retained.ServiceRecords.Count.ShouldBe(rtrOnly ? 1 : 0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_or_changed_departure_requires_service_end_even_when_all_employee_periods_are_closed(bool existingDeparture)
    {
        await using var fixture = await Fixture.CreateAsync(existingDeparture: existingDeparture, closedService: false);
        var service = (await fixture.ReadAsync()).Services.ShouldHaveSingleItem();

        var result = await fixture.SaveDepartureAsync(Now.AddMinutes(90));

        result.Error.Code.ShouldBe("Operations.Departure.ServiceWorkIncomplete");
        result.Error.Failures!.Keys.ShouldContain($"Services[{service.Id}].EndUtc");
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Legacy_closed_service_with_an_open_employee_period_cannot_be_departed()
    {
        await using var fixture = await Fixture.CreateAsync(closedPerformer: false);

        var result = await fixture.SaveDepartureAsync(Now.AddMinutes(90));

        result.Error.Code.ShouldBe("Operations.Departure.ServiceWorkIncomplete");
        result.Error.Failures!.Keys.ShouldContain(key => key.Contains(".Performers[") && key.EndsWith(".EndUtc"));
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Retained_service_without_a_performer_cannot_be_departed()
    {
        await using var fixture = await Fixture.CreateAsync(withPerformers: false);

        var result = await fixture.SaveDepartureAsync(Now.AddMinutes(90));

        result.Error.Code.ShouldBe("Operations.Departure.ServiceWorkIncomplete");
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Departure_with_closed_services_and_closed_employee_periods_is_saved()
    {
        await using var fixture = await Fixture.CreateAsync(existingDeparture: false);

        var result = await fixture.SaveDepartureAsync(Now.AddMinutes(90));

        result.IsSuccess.ShouldBeTrue();
        fixture.Writes.SaveCalls.ShouldBe(1);
        fixture.Storage.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Departure_remarks_correction_requires_complete_work_even_if_actual_departure_time_is_unchanged()
    {
        await using var fixture = await Fixture.CreateAsync(closedService: false);

        var result = await fixture.SaveDepartureAsync(fixture.InitialDeparture, remarks: "New remarks require a new accepted write");

        result.Error.Code.ShouldBe("Operations.Departure.ServiceWorkIncomplete");
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Exact_departure_replay_still_acknowledges_before_completion_guard_when_retained_work_is_open()
    {
        await using var fixture = await Fixture.CreateAsync(closedService: false, closedPerformer: false);
        await fixture.ChangeVersionAsync();

        var result = await fixture.SaveDepartureAsync(fixture.InitialDeparture, version: InitialVersion);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RowVersion.ShouldNotBe(Convert.ToBase64String(InitialVersion));
        fixture.Writes.SaveCalls.ShouldBe(0);
        fixture.Storage.SaveCalls.ShouldBe(0);
    }

    private enum Work { None, Normal, Rtr }

    private sealed class Fixture : IAsyncDisposable
    {
        public required OperationsDbContext Db { get; init; }
        public required WriteBoundary Writes { get; init; }
        public required MemoryStorage Storage { get; init; }
        public required Reader MasterData { get; init; }
        public required MobileFlightScopeContext Actor { get; init; }
        public required Guid FlightId { get; init; }
        public required DateTimeOffset InitialArrival { get; init; }
        public required DateTimeOffset InitialDeparture { get; init; }

        public async Task<Result<FlightArrivalSavedDto>> SaveArrivalAsync(DateTimeOffset ata, string tail = "HZ-TEST", byte[]? version = null)
        {
            var current = version ?? (await Db.Flights.SingleAsync(f => f.Id == FlightId)).RowVersion;
            var handler = new SaveFlightArrivalCommandHandler(Writes, new Scope(Actor), new MasterDataResolver(MasterData),
                new Clock(), NoopMobileSyncBroadcaster.Instance);
            return await handler.Handle(new(FlightId, new(ata, MasterData.AircraftId, tail, "REF-ARR", "Arrival"), current), CancellationToken.None);
        }

        public async Task<Result<FlightDepartureSavedDto>> SaveDepartureAsync(DateTimeOffset atd, string remarks = "Departure", byte[]? version = null)
        {
            var current = await Db.Flights.Include(f => f.DepartureSubmissions).SingleAsync(f => f.Id == FlightId);
            var own = current.DepartureSubmissions.Any(d => d.SubmittedByEmployee.StaffMemberId == Actor.StaffMemberId);
            var input = new SaveFlightDepartureRequest(atd, own ? null : Png, own ? null : Now, remarks);
            var handler = new SaveFlightDepartureCommandHandler(Writes, new Scope(Actor), new MasterDataResolver(MasterData),
                new Clock(), NoopMobileSyncBroadcaster.Instance, Storage);
            return await handler.Handle(new(FlightId, input, version ?? current.RowVersion), CancellationToken.None);
        }

        public async Task<Flight> ReadAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Flights.Include(f => f.ArrivalSubmissions).Include(f => f.DepartureSubmissions)
                .Include(f => f.ServiceRecords).ThenInclude(s => s.Performers).SingleAsync(f => f.Id == FlightId);
        }

        public async Task ChangeVersionAsync()
        {
            var flight = await Db.Flights.SingleAsync(f => f.Id == FlightId);
            Db.Entry(flight).Property(f => f.RowVersion).CurrentValue = [2, 0, 0, 0, 0, 0, 0, 0];
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public static async Task<Fixture> CreateAsync(bool existingArrival = true, bool existingDeparture = true,
            bool closedService = true, bool closedPerformer = true, DateTimeOffset? arrival = null,
            DateTimeOffset? departure = null, Work work = Work.Normal, bool withPerformers = true)
        {
            var db = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var inner = new SchedulingMasterDataReader();
            var staffId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            inner.StaffMembers[staffId] = new(staffId, "Operating employee", "E1", inner.StationId, Guid.NewGuid(), true);
            var reader = new Reader(inner);
            var employee = new StaffMemberSnapshot(staffId, "Operating employee", "E1");
            var initialArrival = arrival ?? Now.AddMinutes(-30);
            var initialDeparture = departure ?? (work == Work.Rtr ? Now.AddHours(4) : Now.AddHours(1));
            var flight = Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Customer"),
                new StationSnapshot(inner.StationId, "RUH", "Riyadh"), new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
                FlightNumber.Create("SV100", "SV").Value, ScheduledTime.Create(Now.AddHours(-1), Now.AddHours(1)).Value,
                null, [new ServiceSnapshot(inner.ServiceId, "Service")], [employee], userId, Now).Value;
            if (existingArrival)
                flight.SubmitArrival(employee, ArrivalDetails.Create("REF-ARR", initialArrival,
                    new AircraftTypeSnapshot(reader.AircraftId, "Airbus", "A320"), "HZ-TEST", employee, "Arrival").Value,
                    userId, Now).IsSuccess.ShouldBeTrue();
            if (existingDeparture || work == Work.Rtr)
                flight.SubmitDeparture(employee, DepartureDetails.Create(initialDeparture,
                    CustomerSignature.Create("signature.png", "seed/signature.png", "image/png", Png.Length, Now).Value,
                    employee, "Departure").Value, userId, Now).IsSuccess.ShouldBeTrue();
            if (work == Work.Normal)
            {
                var window = TimeWindow.Create(Now, closedService ? Now.AddMinutes(30) : null).Value;
                var service = flight.AddService(new ServiceSnapshot(inner.ServiceId, "Service"), null,
                    closedService && !closedPerformer ? TimeWindow.Create(Now, null).Value : window, null, userId, Now,
                    initialPerformers: withPerformers
                        ? [new(employee, TimeWindow.Create(Now.AddMinutes(10), closedPerformer ? Now.AddMinutes(20) : null).Value)]
                        : []).Value;
                if (closedService && !closedPerformer)
                {
                    // Import a pre-rule persisted graph; new domain writes cannot create this state.
                    typeof(FlightService).GetProperty(nameof(FlightService.Window))!.SetValue(service, window);
                    typeof(FlightService).GetProperty(nameof(FlightService.EndedByUserId))!.SetValue(service, userId);
                }
            }
            else if (work == Work.Rtr)
            {
                var occurrence = flight.StartReturnToRamp(RtrArrivalDetails.Create(Now.AddHours(2), employee, null).Value, userId, Now).Value;
                flight.AddService(new ServiceSnapshot(inner.ServiceId, "Service"), null,
                    TimeWindow.Create(Now.AddMinutes(135), null).Value, null, userId, Now, occurrence.Id,
                    [new(employee, TimeWindow.Create(Now.AddMinutes(140), null).Value)]).IsSuccess.ShouldBeTrue();
            }
            db.Flights.Add(flight);
            db.Entry(flight).Property(f => f.RowVersion).CurrentValue = InitialVersion.ToArray();
            await db.SaveChangesAsync();
            return new Fixture
            {
                Db = db, Writes = new WriteBoundary(db), Storage = new MemoryStorage(), MasterData = reader,
                Actor = new(inner.StationId, staffId, userId, Now), FlightId = flight.Id,
                InitialArrival = initialArrival, InitialDeparture = initialDeparture
            };
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class WriteBoundary(OperationsDbContext db) : IOperationsDbContext
    {
        public int SaveCalls { get; private set; }
        public DbSet<Flight> Flights => db.Flights;
        public DbSet<OutboxMessage> OutboxMessages => db.OutboxMessages;
        public DbSet<InboxMessage> InboxMessages => db.InboxMessages;
        public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
            db.SetOriginalRowVersion(entity, rowVersion);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public int SaveCalls { get; private set; }
        public async Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            return new($"{container}/{Guid.NewGuid():N}.png", contentType, buffer.Length);
        }
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new MemoryStream(Png));
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Scope(MobileFlightScopeContext actor) : IMobileFlightScope
    {
        public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(actor));
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class Reader(SchedulingMasterDataReader inner) : IMasterDataReader
    {
        public Guid AircraftId { get; } = Guid.NewGuid();
        public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<AircraftTypeReadSnapshot?>(new(id, "Airbus", "A320", true));
        public Task<AtaChapterReadSnapshot?> GetAtaChapterAsync(Guid id, CancellationToken ct) => inner.GetAtaChapterAsync(id, ct);
        public Task<IReadOnlyList<AtaChapterReadSnapshot>> GetActiveAtaChaptersAsync(CancellationToken ct) => inner.GetActiveAtaChaptersAsync(ct);
        public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken ct) => inner.GetCustomerAsync(id, ct);
        public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken ct) => inner.GetStationAsync(id, ct);
        public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken ct) => inner.GetOperationTypeAsync(id, ct);
        public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken ct) => inner.GetServiceAsync(id, ct);
        public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.GetServicesAsync(ids, ct);
        public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken ct) => inner.GetStaffMemberAsync(id, ct);
        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.GetStaffMembersAsync(ids, ct);
        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(Guid id, CancellationToken ct) => inner.GetActiveStaffMembersForStationAsync(id, ct);
        public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken ct) => inner.GetToolAsync(id, ct);
        public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken ct) => inner.GetMaterialAsync(id, ct);
        public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken ct) => inner.GetGeneralSupportAsync(id, ct);
        public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(Guid id, CancellationToken ct) => inner.GetManpowerTypeAsync(id, ct);
        public Task<IReadOnlySet<Guid>> GetAllowedActiveServiceIdsAsync(Guid id, CancellationToken ct) => inner.GetAllowedActiveServiceIdsAsync(id, ct);
        public Task<IReadOnlyList<ServiceReadSnapshot>> GetActiveServicesAsync(CancellationToken ct) => inner.GetActiveServicesAsync(ct);
        public Task<IReadOnlyList<ToolReadSnapshot>> GetActiveToolsAsync(CancellationToken ct) => inner.GetActiveToolsAsync(ct);
        public Task<IReadOnlyList<MaterialReadSnapshot>> GetActiveMaterialsAsync(CancellationToken ct) => inner.GetActiveMaterialsAsync(ct);
        public Task<IReadOnlyList<GeneralSupportReadSnapshot>> GetActiveGeneralSupportsAsync(CancellationToken ct) => inner.GetActiveGeneralSupportsAsync(ct);
        public Task<IReadOnlyList<CustomerReadSnapshot>> GetActiveCustomersAsync(CancellationToken ct) => inner.GetActiveCustomersAsync(ct);
        public Task<IReadOnlyList<AircraftTypeReadSnapshot>> GetActiveAircraftTypesAsync(CancellationToken ct) => inner.GetActiveAircraftTypesAsync(ct);
    }
}
