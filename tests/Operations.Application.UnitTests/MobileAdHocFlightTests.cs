using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
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

namespace Operations.Application.UnitTests;

public sealed class MobileAdHocFlightTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Details_only_creation_uses_entered_number_without_customer_prefix_and_preserves_identity_metadata()
    {
        await using var f = new Fixture(); var id = Guid.NewGuid();
        var input = f.Input with { FlightNumber = " ferry/x ", ScheduledArrivalUtc = Now.ToOffset(TimeSpan.FromHours(3)),
            ScheduledDepartureUtc = Now.AddHours(1).ToOffset(TimeSpan.FromHours(3)) };

        var result = await f.CreateAsync(id, input);

        result.IsSuccess.ShouldBeTrue(); var saved = result.Value;
        saved.IsAdHocFlight.ShouldBeTrue();
        saved.Id.ShouldBe(id); saved.FlightNumber.ShouldBe("FERRY/X"); saved.CustomerIataCode.ShouldBe("SV");
        saved.StationId.ShouldBe(f.Owner.StationId); saved.OperationTypeId.ShouldBe(WellKnownMasterDataIds.AdHocOperationType);
        saved.PlannedServices.ShouldBeEmpty(); saved.AssignedEmployees.ShouldBeEmpty(); saved.AircraftTypeId.ShouldBeNull();
        saved.ArrivalSubmissions!.ShouldBeEmpty(); saved.Services!.ShouldBeEmpty(); saved.Tasks!.ShouldBeEmpty();
        saved.ScheduledArrivalUtc.ShouldBe(Now); saved.ScheduledArrivalUtc.Offset.ShouldBe(TimeSpan.Zero);
        saved.CreatedByUserId.ShouldBe(f.Owner.UserId); saved.CreatedByDisplayName.ShouldBe("Creator account");
        saved.CreatedAtUtc.ShouldBe(Now); saved.UpdatedAtUtc.ShouldBeNull(); saved.CanEditFlightDetails.ShouldBeTrue();
        f.Writes.SaveCalls.ShouldBe(1);
        var flight = await f.ReadAsync(id); flight.AdHocCreationFingerprint.ShouldNotBeNull().Length.ShouldBe(64);
    }

    [Theory]
    [InlineData("empty-id")]
    [InlineData("empty-customer")]
    [InlineData("blank-number")]
    [InlineData("long-number")]
    [InlineData("missing-arrival")]
    [InlineData("missing-departure")]
    [InlineData("departure-before-arrival")]
    [InlineData("outside-window")]
    public async Task Invalid_scratch_details_do_not_publish_a_flight(string invalid)
    {
        await using var f = new Fixture();
        var input = invalid switch
        {
            "empty-customer" => f.Input with { CustomerId = Guid.Empty },
            "blank-number" => f.Input with { FlightNumber = "  " },
            "long-number" => f.Input with { FlightNumber = "1234567890123" },
            "missing-arrival" => f.Input with { ScheduledArrivalUtc = default },
            "missing-departure" => f.Input with { ScheduledDepartureUtc = default },
            "departure-before-arrival" => f.Input with { ScheduledDepartureUtc = f.Input.ScheduledArrivalUtc.AddTicks(-1) },
            "outside-window" => f.Input with { ScheduledArrivalUtc = Now.AddHours(13), ScheduledDepartureUtc = Now.AddHours(14) },
            _ => f.Input
        };
        var result = await f.CreateAsync(invalid == "empty-id" ? Guid.Empty : Guid.NewGuid(), input);
        result.IsFailure.ShouldBeTrue(); f.Writes.SaveCalls.ShouldBe(0);
        (await f.Db.Flights.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(12)]
    public async Task Equal_schedule_instants_at_inclusive_mobile_window_edges_are_valid(int hours)
    {
        await using var f = new Fixture(); var instant = Now.AddHours(hours);
        var result = await f.CreateAsync(Guid.NewGuid(), f.Input with
        { CustomerId = WellKnownMasterDataIds.UnknownCustomer, FlightNumber = "123456789012", ScheduledArrivalUtc = instant, ScheduledDepartureUtc = instant });
        result.IsSuccess.ShouldBeTrue(); result.Value.CustomerId.ShouldBe(WellKnownMasterDataIds.UnknownCustomer);
        result.Value.ScheduledArrivalUtc.ShouldBe(instant); result.Value.ScheduledDepartureUtc.ShouldBe(instant);
    }

    [Fact]
    public async Task Original_creation_retry_acknowledges_after_details_and_foreign_work_change_without_replacing_current_values()
    {
        await using var f = new Fixture(); var id = Guid.NewGuid(); var original = f.Input with { FlightNumber = " ferry-1 " };
        var first = await f.CreateAsync(id, original); first.IsSuccess.ShouldBeTrue();
        var fingerprint = (await f.ReadAsync(id)).AdHocCreationFingerprint;
        f.Clock.UtcNow = Now.AddMinutes(1);
        var changed = f.Input with { CustomerId = Guid.NewGuid(), FlightNumber = "charter", ScheduledDepartureUtc = Now.AddHours(2) };
        var updated = await f.UpdateAsync(id, changed, first.Value.RowVersion); updated.IsSuccess.ShouldBeTrue();
        await f.SeedWorkAsync(id, "service"); var before = await f.ReadAsync(id); var writes = f.Writes.SaveCalls;

        var replay = await f.CreateAsync(id, original with { FlightNumber = "FERRY-1" });

        replay.IsSuccess.ShouldBeTrue(); replay.Value.FlightNumber.ShouldBe("CHARTER"); replay.Value.CustomerId.ShouldBe(changed.CustomerId);
        replay.Value.CreatedAtUtc.ShouldBe(first.Value.CreatedAtUtc); replay.Value.CanEditFlightDetails.ShouldBeFalse();
        replay.Value.UpdatedAtUtc.ShouldBe(before.UpdatedAtUtc); f.Writes.SaveCalls.ShouldBe(writes);
        (await f.ReadAsync(id)).AdHocCreationFingerprint.ShouldBe(fingerprint);
        (await f.Db.Flights.AsNoTracking().CountAsync()).ShouldBe(1);
        (await f.CreateAsync(id, original with { CustomerId = Guid.NewGuid() })).Error.Code.ShouldBe("Operations.AdHoc.IdConflict");
        f.Writes.SaveCalls.ShouldBe(writes);
    }

    [Theory]
    [InlineData("arrival")]
    [InlineData("departure")]
    [InlineData("service")]
    [InlineData("task")]
    [InlineData("cancellation")]
    [InlineData("return-to-ramp")]
    public async Task Persisted_operational_records_by_any_actor_lock_all_changed_flight_details(string kind)
    {
        await using var f = new Fixture(); var id = Guid.NewGuid();
        var created = await f.CreateAsync(id); created.IsSuccess.ShouldBeTrue();
        await f.SeedWorkAsync(id, kind); var before = await f.ReadAsync(id); var writes = f.Writes.SaveCalls;
        var changed = f.Input with { CustomerId = Guid.NewGuid(), FlightNumber = "CHANGED", ScheduledArrivalUtc = Now.AddMinutes(1) };

        var result = await f.UpdateAsync(id, changed, Convert.ToBase64String(before.RowVersion));

        result.Error.Code.ShouldBe("Operations.AdHoc.DetailsLocked"); f.Writes.SaveCalls.ShouldBe(writes);
        var retained = await f.ReadAsync(id); retained.Customer.ShouldBe(before.Customer); retained.FlightNumber.ShouldBe(before.FlightNumber);
        retained.Schedule.ShouldBe(before.Schedule); retained.RowVersion.ShouldBe(before.RowVersion);
        (await f.UpdateAsync(id, f.Input, created.Value.RowVersion)).IsSuccess.ShouldBeTrue();
        f.Writes.SaveCalls.ShouldBe(writes);
    }

    [Fact]
    public async Task Creator_can_edit_before_work_but_stale_changed_input_and_foreign_exact_input_are_rejected()
    {
        await using var f = new Fixture(); var id = Guid.NewGuid();
        var original = await f.CreateAsync(id); original.IsSuccess.ShouldBeTrue();
        f.Clock.UtcNow = Now.AddMinutes(2); var changed = f.Input with { FlightNumber = "UPDATED" };
        var updated = await f.UpdateAsync(id, changed, original.Value.RowVersion); updated.IsSuccess.ShouldBeTrue();
        updated.Value.CreatedByUserId.ShouldBe(original.Value.CreatedByUserId); updated.Value.CreatedAtUtc.ShouldBe(original.Value.CreatedAtUtc);
        updated.Value.UpdatedAtUtc.ShouldBe(f.Clock.UtcNow); updated.Value.CanEditFlightDetails.ShouldBeTrue();
        var writes = f.Writes.SaveCalls;
        (await f.UpdateAsync(id, changed with { FlightNumber = "STALE" }, original.Value.RowVersion)).Error.Code.ShouldBe("General.ConcurrencyConflict");
        var other = f.Owner with { StaffMemberId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        (await f.UpdateAsync(id, changed, updated.Value.RowVersion, other)).Error.Code.ShouldBe("Operations.AdHoc.DetailsReadOnly");
        (await f.CreateAsync(id, f.Input, other)).Error.Code.ShouldBe("Operations.AdHoc.IdConflict");
        f.Writes.SaveCalls.ShouldBe(writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scratch_context_exposes_authoritative_station_clock_scope_and_current_service_grants(bool noAllowedServices)
    {
        await using var f = new Fixture();
        if (noAllowedServices) f.Reader.AllowedServicesOverride = new HashSet<Guid>();
        var result = await new GetMobileAdHocFlightContextQueryHandler(new Scope(f.Owner), new MasterDataResolver(f.Reader), f.Reader)
            .Handle(new(), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(); var context = result.Value;
        context.StationId.ShouldBe(f.Reader.StationId); context.StationTimeZoneId.ShouldBe("Asia/Riyadh");
        context.CurrentStaffMemberId.ShouldBe(f.Owner.StaffMemberId); context.CanCreate.ShouldBeTrue();
        context.CanSaveArrival.ShouldBeTrue(); context.CanSaveDeparture.ShouldBeTrue(); context.CanSaveTasks.ShouldBeTrue();
        context.CanSaveServices.ShouldBe(!noAllowedServices);
        context.AllowedServiceIds.ShouldBe(noAllowedServices ? [] : new[] { f.Reader.ServiceId });
        context.AsOfUtc.ShouldBe(Now); context.WindowStartUtc.ShouldBe(Now.AddHours(-12)); context.WindowEndUtc.ShouldBe(Now.AddHours(12));
        f.Writes.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("missing-station", "Operations.Ref.StationNotFound")]
    [InlineData("invalid-zone", "Operations.Ref.StationTimeZoneInvalid")]
    [InlineData("inactive-staff", "Operations.Scope.Denied")]
    public async Task Scratch_context_rejects_missing_station_invalid_iana_zone_or_revoked_staff(string invalid, string code)
    {
        await using var f = new Fixture();
        if (invalid == "missing-station") f.Reader.StationExists = false;
        if (invalid == "invalid-zone") f.Reader.StationTimeZoneId = "Not/AZone";
        if (invalid == "inactive-staff") f.Reader.StaffMembers[f.Owner.StaffMemberId] =
            f.Reader.StaffMembers[f.Owner.StaffMemberId] with { IsActive = false };
        var result = await new GetMobileAdHocFlightContextQueryHandler(new Scope(f.Owner), new MasterDataResolver(f.Reader), f.Reader)
            .Handle(new(), CancellationToken.None);
        result.Error.Code.ShouldBe(code); f.Writes.SaveCalls.ShouldBe(0);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public OperationsDbContext Db { get; } = new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public SchedulingMasterDataReader Reader { get; } = new();
        public WriteBoundary Writes { get; }
        public MobileFlightScopeContext Owner { get; }
        public Clock Clock { get; } = new();
        public WorkUserNames Names { get; } = new();
        public SaveMobileAdHocFlightDetailsRequest Input { get; } = new(Guid.NewGuid(), "FERRY-1", Now, Now.AddHours(1));

        public Fixture()
        {
            Writes = new(Db); Owner = new(Reader.StationId, Guid.NewGuid(), Guid.NewGuid(), Now);
            Reader.StaffMembers[Owner.StaffMemberId] = new(Owner.StaffMemberId, "Creator employee", "E1", Reader.StationId, Owner.UserId, true);
            Names.Values[Owner.UserId] = "Creator account";
        }

        public Task<Result<FlightDetailDto>> CreateAsync(Guid id, SaveMobileAdHocFlightDetailsRequest? input = null, MobileFlightScopeContext? actor = null) =>
            new CreateMobileAdHocFlightCommandHandler(Writes, new Scope(actor ?? Owner), new MasterDataResolver(Reader), Reader,
                Clock, NoopMobileSyncBroadcaster.Instance, Names).Handle(new(id, input ?? Input), CancellationToken.None);

        public Task<Result<FlightDetailDto>> UpdateAsync(Guid id, SaveMobileAdHocFlightDetailsRequest input, string version, MobileFlightScopeContext? actor = null) =>
            new UpdateMobileAdHocFlightDetailsCommandHandler(Writes, new Scope(actor ?? Owner), new MasterDataResolver(Reader), Reader,
                Clock, NoopMobileSyncBroadcaster.Instance, Names).Handle(new(id, input, Convert.FromBase64String(version)), CancellationToken.None);

        public async Task<Flight> ReadAsync(Guid id)
        {
            Db.ChangeTracker.Clear();
            return await Db.Flights.Include(item => item.ArrivalSubmissions).Include(item => item.DepartureSubmissions)
                .Include(item => item.CancellationSubmissions).Include(item => item.ReturnToRamps)
                .Include(item => item.ServiceRecords).ThenInclude(item => item.Performers)
                .Include(item => item.TaskRecords).ThenInclude(item => item.Performers).SingleAsync(item => item.Id == id);
        }

        public async Task SeedWorkAsync(Guid id, string kind)
        {
            var flight = await ReadAsync(id); var actor = new StaffMemberSnapshot(Guid.NewGuid(), "Colleague", "E2");
            var actorId = Guid.NewGuid(); var period = TimeWindow.Create(Now, null).Value;
            switch (kind)
            {
                case "arrival":
                    flight.SubmitArrival(actor, ArrivalDetails.Create(null, Now, new(Guid.NewGuid(), "Airbus", "A320"), "HZ-A", actor, null).Value,
                        actorId, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue(); break;
                case "departure":
                    flight.SubmitDeparture(actor, DepartureDetails.Create(Now.AddHours(1), null, actor, null).Value,
                        actorId, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue(); break;
                case "service":
                    flight.AddService(new(Guid.NewGuid(), "Service"), null, period, null, actorId, Now.AddMinutes(3),
                        initialPerformers: [new(actor, period)]).IsSuccess.ShouldBeTrue(); break;
                case "task":
                    flight.AddTask(FlightTaskType.Major, null, null, period, null, actorId, Now.AddMinutes(3),
                        initialPerformers: [new(actor, period)]).IsSuccess.ShouldBeTrue(); break;
                case "cancellation":
                    flight.SubmitCancellation(actor, CancellationDetails.Create(Now, null).Value, actorId, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue(); break;
                case "return-to-ramp":
                    flight.SubmitDeparture(actor, DepartureDetails.Create(Now, null, actor, null).Value, actorId, Now).IsSuccess.ShouldBeTrue();
                    flight.StartReturnToRamp(RtrArrivalDetails.Create(Now.AddMinutes(1), actor, null).Value, actorId, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue(); break;
            }
            Db.Entry(flight).Property(item => item.RowVersion).CurrentValue = [90, 0, 0, 0, 0, 0, 0, 0];
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Scope(MobileFlightScopeContext context) : IMobileFlightScope
    {
        public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(context));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class WriteBoundary(OperationsDbContext db) : IOperationsDbContext
    {
        public int SaveCalls { get; private set; }
        public DbSet<Flight> Flights => db.Flights;
        public DbSet<FlightUserActivity> FlightUserActivities => db.FlightUserActivities;
        public DbSet<OutboxMessage> OutboxMessages => db.OutboxMessages;
        public DbSet<InboxMessage> InboxMessages => db.InboxMessages;
        public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class => db.SetOriginalRowVersion(entity, rowVersion);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            foreach (var entry in db.ChangeTracker.Entries<Flight>()) entry.Property(item => item.RowVersion).CurrentValue = BitConverter.GetBytes((long)SaveCalls);
            return db.SaveChangesAsync(cancellationToken);
        }
    }
}
