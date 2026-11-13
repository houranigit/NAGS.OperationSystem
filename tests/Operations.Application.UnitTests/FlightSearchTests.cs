using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Authorization;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Version = [1, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public async Task Exact_discovery_includes_unassigned_regular_only_and_never_widens_normal_feed()
    {
        await using var f = new Fixture(); var regular = f.NewFlight();
        f.Db.Flights.AddRange(regular, f.NewFlight("1000"), f.NewFlight(perLanding: true), f.NewFlight(adHoc: true),
            f.NewFlight(station: Guid.NewGuid()), f.NewFlight(sta: Now.AddHours(13), std: Now.AddHours(14)));
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var found = await new GetMobileFlightSearchQueryHandler(f.Db, f.Scope, NoopCustomerLogoReader.Instance, f.Reader)
            .Handle(new(" sv100 "), CancellationToken.None);
        found.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(regular.Id);
        var normal = await new GetMobileFlightsQueryHandler(f.Db, f.Scope, NoopCustomerLogoReader.Instance, f.Reader)
            .Handle(new(Category: "MyFlights"), CancellationToken.None);
        normal.Value.Items.ShouldBeEmpty();
        (await f.Db.FlightUserActivities.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task First_combined_arrival_service_and_task_can_publish_together_but_later_additions_are_denied()
    {
        await using var f = new Fixture(); var flight = await f.PersistAsync(f.NewFlight());
        var serviceId = Guid.NewGuid(); var taskId = Guid.NewGuid();
        var first = await f.BatchAsync(flight.Id, new(f.Arrival, ServiceChanges: [new(serviceId, f.Service)], TaskChanges: [new(taskId, f.Task)]));
        first.IsSuccess.ShouldBeTrue(); first.Value.IsUnassignedSearchFlight.ShouldBeTrue();
        first.Value.CanAddServices.ShouldBe(false); first.Value.CanAddTasks.ShouldBe(false);
        first.Value.AssignedEmployees.ShouldBeEmpty();
        var rejected = await f.BatchAsync(flight.Id, new(ServiceChanges: [new(Guid.NewGuid(), f.Service with { Performers = [new(Guid.NewGuid(), f.Actor.StaffMemberId, Now)] })]));
        rejected.Error.Code.ShouldBe("Operations.Search.NewWorkReadOnly");
        var corrected = await f.SaveServiceAsync(flight.Id, serviceId, f.Service with { Notes = "Own correction" });
        corrected.IsSuccess.ShouldBeTrue(); corrected.Value.Service.CanEdit.ShouldBeTrue();
        (await f.Db.FlightUserActivities.SingleAsync()).UsedSearch.ShouldBeTrue();
    }

    [Fact]
    public async Task Arrival_only_first_save_prevents_later_new_work_for_an_unassigned_searcher()
    {
        await using var f = new Fixture(); var flight = await f.PersistAsync(f.NewFlight());
        (await f.BatchAsync(flight.Id, new(Arrival: f.Arrival))).IsSuccess.ShouldBeTrue();
        (await f.SaveServiceAsync(flight.Id, Guid.NewGuid(), f.Service)).Error.Code.ShouldBe("Operations.Search.NewWorkReadOnly");
        (await f.ReadAsync(flight.Id)).Services.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("reference")]
    [InlineData("start")]
    [InlineData("service")]
    [InlineData("closed-child-end")]
    [InlineData("staff-id")]
    [InlineData("parent-open")]
    public async Task Foreign_service_closure_preserves_all_core_and_already_closed_staff_fields(string tamper)
    {
        await using var f = new Fixture(); var seeded = await f.SeedForeignWorkAsync();
        var desired = seeded.Service with { EndUtc = Now.AddMinutes(30), Performers = seeded.Service.Performers.Select(item =>
            item.EndUtc.HasValue ? item : item with { EndUtc = Now.AddMinutes(25) }).ToArray() };
        desired = tamper switch
        {
            "notes" => desired with { Notes = "Foreign edit" },
            "reference" => desired with { CustomerReferenceNumber = "NEW" },
            "start" => desired with { StartUtc = desired.StartUtc.AddMinutes(-1) },
            "service" => desired with { ServiceId = Guid.NewGuid() },
            "closed-child-end" => desired with { Performers = desired.Performers.Select((item, index) => index == 0 ? item with { EndUtc = Now.AddMinutes(21) } : item).ToArray() },
            "staff-id" => desired with { Performers = desired.Performers.Select((item, index) => index == 0 ? item with { Id = Guid.NewGuid() } : item).ToArray() },
            _ => desired with { EndUtc = null }
        };
        var result = await f.SaveServiceAsync(seeded.FlightId, seeded.ServiceId, desired);
        result.IsFailure.ShouldBeTrue(); f.Writes.SaveCalls.ShouldBe(0);
        (await f.ReadAsync(seeded.FlightId)).Services.Single().Window.ToUtc.ShouldBeNull();
        (await f.Db.FlightUserActivities.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Foreign_service_can_close_outside_catalog_allowance_and_exact_retry_does_not_touch_history()
    {
        await using var f = new Fixture(); var seeded = await f.SeedForeignWorkAsync(); f.Reader.AllowedServicesOverride = new HashSet<Guid>();
        var detail = await f.DetailAsync(seeded.FlightId); var card = detail.Value.Services!.Single();
        card.CanCloseOnly.ShouldBeTrue(); card.CanEdit.ShouldBeFalse(); card.CanDelete.ShouldBeFalse();
        detail.Value.CanAddServices.ShouldBe(false);
        var closed = seeded.Service with { EndUtc = Now.AddMinutes(30), Performers = seeded.Service.Performers.Select(item =>
            item.EndUtc.HasValue ? item : item with { EndUtc = Now.AddMinutes(25) }).ToArray() };
        var result = await f.SaveServiceAsync(seeded.FlightId, seeded.ServiceId, closed);
        result.IsSuccess.ShouldBeTrue(); result.Value.Service.CanSetEnd.ShouldBeFalse(); result.Value.Service.CreatedByUserId.ShouldBe(f.AuthorUserId);
        var writes = f.Writes.SaveCalls; var before = (await f.Db.FlightUserActivities.SingleAsync()).LastInteractionAtUtc;
        (await f.SaveServiceAsync(seeded.FlightId, seeded.ServiceId, closed, Version)).IsSuccess.ShouldBeTrue();
        f.Writes.SaveCalls.ShouldBe(writes); (await f.Db.FlightUserActivities.SingleAsync()).LastInteractionAtUtc.ShouldBe(before);
        (await f.SaveServiceAsync(seeded.FlightId, seeded.ServiceId, closed with { EndUtc = Now.AddMinutes(31) })).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Combined_foreign_closure_fills_missing_staff_and_duration_ends_and_rejects_quantity_or_closed_end_changes()
    {
        await using var f = new Fixture(); var seeded = await f.SeedForeignWorkAsync(withTask: true);
        var closedService = seeded.Service with { EndUtc = Now.AddMinutes(30), Performers = seeded.Service.Performers.Select(item => item.EndUtc.HasValue ? item : item with { EndUtc = Now.AddMinutes(25) }).ToArray() };
        var task = seeded.Task!;
        var closedTask = task with { EndUtc = Now.AddMinutes(30), Performers = task.Performers.Select(item => item with { EndUtc = Now.AddMinutes(25) }).ToArray(),
            Tools = task.Tools.Select(item => item with { EndUtc = item.EndUtc ?? Now.AddMinutes(25) }).ToArray() };
        var rejected = await f.BatchAsync(seeded.FlightId, new(TaskChanges: [new(seeded.TaskId!.Value, closedTask with { Tools = closedTask.Tools.Select((item, index) => index == 0 ? item with { Quantity = 1 } : item).ToArray() })]));
        rejected.IsFailure.ShouldBeTrue(); f.Writes.SaveCalls.ShouldBe(0); f.Db.ChangeTracker.Clear();
        var result = await f.BatchAsync(seeded.FlightId, new(Departure: new(Now.AddMinutes(35), null, null, null),
            ServiceChanges: [new(seeded.ServiceId, closedService)], TaskChanges: [new(seeded.TaskId.Value, closedTask)]));
        result.IsSuccess.ShouldBeTrue(result.Error.ToString()); result.Value.DepartureSubmissions!.Single().HasSignature.ShouldBeFalse();
        result.Value.Services!.Single().CanSetEnd.ShouldBeFalse(); result.Value.Tasks!.Single().CanSetEnd.ShouldBeFalse();
        result.Value.AssignedEmployees.ShouldBeEmpty();
        (await f.Db.FlightUserActivities.SingleAsync()).UserId.ShouldBe(f.Actor.UserId);
    }

    [Theory]
    [InlineData("closed-end")]
    [InlineData("start")]
    [InlineData("quantity")]
    public async Task Foreign_task_closure_cannot_rewrite_duration_resource_facts(string tamper)
    {
        await using var f = new Fixture(); var seeded = await f.SeedForeignWorkAsync(withTask: true);
        var desired = seeded.Task! with { EndUtc = Now.AddMinutes(30),
            Performers = seeded.Task!.Performers.Select(item => item with { EndUtc = Now.AddMinutes(25) }).ToArray(),
            Tools = seeded.Task.Tools.Select(item => item with { EndUtc = item.EndUtc ?? Now.AddMinutes(25) }).ToArray() };
        desired = desired with { Tools = desired.Tools.Select((item, index) => tamper switch
        {
            "closed-end" when index == 1 => item with { EndUtc = Now.AddMinutes(11) },
            "start" when index == 0 => item with { StartUtc = Now.AddMinutes(1) },
            "quantity" when index == 0 => item with { Quantity = 2 },
            _ => item
        }).ToArray() };
        var result = await f.BatchAsync(seeded.FlightId, new(TaskChanges: [new(seeded.TaskId!.Value, desired)]));
        result.Error.Code.ShouldBe("Operations.Search.ClosureReadOnly");
        f.Writes.SaveCalls.ShouldBe(0); f.Db.ChangeTracker.Clear();
        (await f.Db.FlightUserActivities.CountAsync()).ShouldBe(0);
    }

    private sealed record Seeded(Guid FlightId, Guid ServiceId, SaveFlightServiceRequest Service, Guid? TaskId = null, SaveFlightTaskRequest? Task = null);
    private sealed class Fixture : IAsyncDisposable
    {
        public OperationsDbContext Db { get; } = new(new DbContextOptionsBuilder<OperationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public SchedulingMasterDataReader Reader { get; } = new();
        public MobileFlightScopeContext Actor { get; }
        public Scope Scope { get; }
        public WriteBoundary Writes { get; }
        public Guid AuthorUserId { get; } = Guid.NewGuid();
        private StaffMemberSnapshot Author { get; } = new(Guid.NewGuid(), "Original employee", "E2");
        private Guid AircraftId { get; } = Guid.NewGuid();
        public SaveFlightArrivalRequest Arrival => new(Now.AddHours(-1), AircraftId, "hz-test", null, null);
        public SaveFlightServiceRequest Service => new(Reader.ServiceId, Now, null, "ref", "Work", [new(Guid.NewGuid(), Actor.StaffMemberId, Now)]);
        public SaveFlightTaskRequest Task => new(0, null, Now, null, null, "Task", [new(Guid.NewGuid(), Actor.StaffMemberId, Now)], [], [], []);
        public Fixture()
        {
            Actor = new(Reader.StationId, Guid.NewGuid(), Guid.NewGuid(), Now); Scope = new(Actor); Writes = new(Db);
            Reader.StaffMembers[Actor.StaffMemberId] = new(Actor.StaffMemberId, "Searcher", "E1", Reader.StationId, Actor.UserId, true);
            Reader.AircraftTypes[AircraftId] = new(AircraftId, "Boeing", "737", true);
        }
        public Flight NewFlight(string number = "100", bool perLanding = false, bool adHoc = false, Guid? station = null, DateTimeOffset? sta = null, DateTimeOffset? std = null) =>
            Flight.ScheduleNew(new(Guid.NewGuid(), "SV", "Customer"), new(station ?? Reader.StationId, "RUH", "Riyadh"),
                new(adHoc ? WellKnownMasterDataIds.AdHocOperationType : Guid.NewGuid(), "Operation"), FlightNumber.Create(number, "SV").Value,
                ScheduledTime.Create(sta ?? Now, std ?? Now.AddHours(1)).Value, null,
                [new(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : Reader.ServiceId, "Service")], [], AuthorUserId, Now).Value;
        public async Task<Flight> PersistAsync(Flight flight)
        {
            Db.Flights.Add(flight); Db.Entry(flight).Property(item => item.RowVersion).CurrentValue = Version.ToArray();
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear(); return flight;
        }
        public async Task<Seeded> SeedForeignWorkAsync(bool withTask = false)
        {
            var flight = NewFlight(); var aircraft = new AircraftTypeSnapshot(AircraftId, "Boeing", "737");
            flight.SubmitArrival(Author, ArrivalDetails.Create(null, Arrival.Ata, aircraft, Arrival.AircraftTailNumber, Author, null).Value, AuthorUserId, Now).IsSuccess.ShouldBeTrue();
            var service = Service with { Performers = [new(Guid.NewGuid(), Author.StaffMemberId, Now, Now.AddMinutes(20)), new(Guid.NewGuid(), Guid.NewGuid(), Now)] };
            var serviceId = Guid.NewGuid();
            flight.SaveService(serviceId, new(Reader.ServiceId, "Service"), service.CustomerReferenceNumber, TimeWindow.Create(Now, null).Value, service.Notes,
                service.Performers.Select(item => new FlightServicePerformerInput(new(item.StaffMemberId, "Employee", "E"), TimeWindow.Create(item.StartUtc, item.EndUtc).Value, item.Id)).ToArray(), AuthorUserId, Now).IsSuccess.ShouldBeTrue();
            Guid? taskId = null; SaveFlightTaskRequest? task = null;
            if (withTask)
            {
                var toolId = Guid.NewGuid(); var closedToolId = Guid.NewGuid(); taskId = Guid.NewGuid();
                task = Task with { Performers = [new(Guid.NewGuid(), Author.StaffMemberId, Now)], Tools = [new(Guid.NewGuid(), toolId, StartUtc: Now),
                    new(Guid.NewGuid(), closedToolId, StartUtc: Now, EndUtc: Now.AddMinutes(10))] };
                flight.SaveTask(taskId.Value, (FlightTaskType)task.TaskType, null, null, TimeWindow.Create(Now, null).Value, task.Notes,
                    [new(Author, TimeWindow.Create(Now, null).Value, null, task.Performers[0].Id)],
                    task.Tools.Select(item => new FlightTaskToolInput(new(item.ResourceId, "Duration tool", MasterData.Contracts.Resources.ResourceCalculationType.Duration),
                        ResourceUsage.Create(MasterData.Contracts.Resources.ResourceCalculationType.Duration, null, item.StartUtc, item.EndUtc).Value, null, item.Id)).ToArray(),
                    [], [], AuthorUserId, Now).IsSuccess.ShouldBeTrue();
            }
            await PersistAsync(flight); return new(flight.Id, serviceId, service, taskId, task);
        }
        public async Task<Flight> ReadAsync(Guid id) { Db.ChangeTracker.Clear(); return await Db.Flights.Include(item => item.ServiceRecords).ThenInclude(item => item.Performers).SingleAsync(item => item.Id == id); }
        private async Task<byte[]> CurrentVersion(Guid id) => (await Db.Flights.SingleAsync(item => item.Id == id)).RowVersion;
        public async Task<Result<FlightServiceSavedDto>> SaveServiceAsync(Guid flight, Guid record, SaveFlightServiceRequest input, byte[]? version = null) =>
            await new SaveFlightServiceCommandHandler(Writes, Scope, new(Reader), new Clock(), NoopMobileSyncBroadcaster.Instance, new Storage())
                .Handle(new(flight, record, input, version ?? await CurrentVersion(flight)), CancellationToken.None);
        public async Task<Result<FlightDetailDto>> BatchAsync(Guid id, SaveFlightOperationsBatchRequest input) =>
            await new SaveFlightOperationsBatchCommandHandler(Writes, Scope, new(Reader), Reader, new Clock(), NoopMobileSyncBroadcaster.Instance, new Storage())
                .Handle(new(id, input, await CurrentVersion(id)), CancellationToken.None);
        public Task<Result<FlightDetailDto>> DetailAsync(Guid id) => new GetMobileFlightByIdQueryHandler(Db, Scope, Reader, new User(Actor.UserId)).Handle(new(id), CancellationToken.None);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Scope(MobileFlightScopeContext actor) : IMobileFlightScope { public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken ct) => Task.FromResult(Result.Success(actor)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class User(Guid id) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => id;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.StationStaff;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permission == OperationsPermissions.Flights.Operate;
    }
    private sealed class Storage : IFileStorage { public Task<StoredFile> SaveAsync(string a, string b, string c, Stream d, CancellationToken ct = default) => throw new NotSupportedException(); public Task<Stream?> OpenAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null); public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class WriteBoundary(OperationsDbContext db) : IOperationsDbContext
    {
        public int SaveCalls { get; private set; }
        public DbSet<Flight> Flights => db.Flights; public DbSet<FlightUserActivity> FlightUserActivities => db.FlightUserActivities;
        public DbSet<OutboxMessage> OutboxMessages => db.OutboxMessages; public DbSet<InboxMessage> InboxMessages => db.InboxMessages;
        public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] version) where TEntity : class => db.SetOriginalRowVersion(entity, version);
        public Task<int> SaveChangesAsync(CancellationToken ct = default) { SaveCalls++; return db.SaveChangesAsync(ct); }
    }
}
