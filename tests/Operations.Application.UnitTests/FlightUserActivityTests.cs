using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightUserActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("assigned", false)]
    [InlineData("searched", true)]
    [InlineData("per-landing", false)]
    [InlineData("adhoc", false)]
    public async Task Recorder_stages_once_without_saving_and_derives_search_from_real_assignment_and_flight_kind(string kind, bool usedSearch)
    {
        await using var f = new Fixture();
        var flight = f.Flight("100", assigned: kind == "assigned", perLanding: kind == "per-landing", adHoc: kind == "adhoc");
        f.Db.Flights.Add(flight); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await FlightActivityRecorder.TouchAsync(f.Db, flight, f.Actor, Now.AddMinutes(1), CancellationToken.None);
        await FlightActivityRecorder.TouchAsync(f.Db, flight, f.Actor, Now.AddMinutes(3).ToOffset(TimeSpan.FromHours(3)), CancellationToken.None);
        await FlightActivityRecorder.TouchAsync(f.Db, flight, f.Actor, Now.AddMinutes(2), CancellationToken.None);
        (await f.Db.FlightUserActivities.AsNoTracking().CountAsync()).ShouldBe(0);
        var pending = f.Db.FlightUserActivities.Local.ShouldHaveSingleItem();
        pending.UsedSearch.ShouldBe(usedSearch); pending.UserId.ShouldBe(f.Actor.UserId); pending.FlightId.ShouldBe(flight.Id);
        pending.LastInteractionAtUtc.ShouldBe(Now.AddMinutes(3)); pending.LastInteractionAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        await f.Db.SaveChangesAsync();
        (await f.Db.FlightUserActivities.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Older_interaction_and_later_assignment_cannot_erase_max_recency_or_prior_search_use()
    {
        await using var f = new Fixture(); var flight = f.Flight("101");
        f.Db.Flights.Add(flight); await f.Db.SaveChangesAsync();
        await FlightActivityRecorder.TouchAsync(f.Db, flight, f.Actor, Now.AddMinutes(10), CancellationToken.None);
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var loaded = await f.Db.Flights.Include(item => item.AssignedEmployees).SingleAsync();
        loaded.ReplaceAssignedEmployees([new(f.Actor.StaffMemberId, "Newly assigned", "E1")], Now).IsSuccess.ShouldBeTrue();
        await FlightActivityRecorder.TouchAsync(f.Db, loaded, f.Actor, Now.AddMinutes(5), CancellationToken.None);
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var saved = await f.Db.FlightUserActivities.AsNoTracking().SingleAsync();
        saved.UsedSearch.ShouldBeTrue(); saved.LastInteractionAtUtc.ShouldBe(Now.AddMinutes(10));
    }

    [Fact]
    public async Task History_uses_account_not_device_or_current_assignment_and_pages_recent_work_with_stable_ties()
    {
        await using var f = new Fixture();
        var first = f.Flight("101", id: Id(1)); var second = f.Flight("102", id: Id(2));
        var third = f.Flight("103", perLanding: true, id: Id(3)); var fourth = f.Flight("104", adHoc: true, id: Id(4));
        var viewed = f.Flight("105"); var otherAccount = f.Flight("106");
        var otherStation = f.Flight("107", stationId: Guid.NewGuid());
        var expired = f.Flight("108", schedule: ScheduledTime.Create(Now.AddHours(-14), Now.AddHours(-13)).Value);
        var future = f.Flight("109", schedule: ScheduledTime.Create(Now.AddHours(13), Now.AddHours(14)).Value);
        f.Db.Flights.AddRange(first, second, third, fourth, viewed, otherAccount, otherStation, expired, future);
        f.Db.FlightUserActivities.AddRange(
            FlightUserActivity.Create(f.Actor.UserId, first.Id, Now.AddMinutes(4)),
            FlightUserActivity.Create(f.Actor.UserId, second.Id, Now.AddMinutes(3)),
            FlightUserActivity.Create(f.Actor.UserId, third.Id, Now.AddMinutes(3)),
            FlightUserActivity.Create(f.Actor.UserId, fourth.Id, Now.AddMinutes(2)),
            FlightUserActivity.Create(Guid.NewGuid(), otherAccount.Id, Now.AddMinutes(9)),
            FlightUserActivity.Create(f.Actor.UserId, otherStation.Id, Now.AddMinutes(9)),
            FlightUserActivity.Create(f.Actor.UserId, expired.Id, Now.AddMinutes(9)),
            FlightUserActivity.Create(f.Actor.UserId, future.Id, Now.AddMinutes(9)));
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var page = await f.HistoryAsync(new(PageSize: 2));
        page.IsSuccess.ShouldBeTrue(); page.Value.TotalCount.ShouldBe(4);
        page.Value.Items.Select(item => item.Id).ShouldBe([first.Id, second.Id]);
        page.Value.Items[0].LastInteractionAtUtc.ShouldBe(Now.AddMinutes(4));
        page.Value.Items.ShouldAllBe(item => item.CreatedByDisplayName == "Planning creator" && item.CustomerLogoVersion == "logo-v1");
        page.Value.Items.ShouldAllBe(item => item.StationTimeZoneId == "Asia/Riyadh");
        var next = await f.HistoryAsync(new(Page: 2, PageSize: 2));
        next.Value.Items.Select(item => item.Id).ShouldBe([third.Id, fourth.Id]);
        next.Value.HasNextPage.ShouldBeFalse();
        var otherDeviceActor = f.Actor with { StaffMemberId = Guid.NewGuid() };
        await using var anotherDevice = new OperationsDbContext(f.Options);
        var onOtherDevice = await new GetMobileFlightHistoryQueryHandler(anotherDevice, new Scope(otherDeviceActor), f.Logos, f.Reader, f.Names)
            .Handle(new(PageSize: 100), CancellationToken.None);
        onOtherDevice.Value.Items.Select(item => item.Id).ShouldBe([first.Id, second.Id, third.Id, fourth.Id]);
        (await f.Db.FlightUserActivities.AsNoTracking().CountAsync()).ShouldBe(8);
        f.Db.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_history_is_saved_regular_search_work_only_even_if_other_kinds_have_a_sticky_flag()
    {
        await using var f = new Fixture();
        var searched = f.Flight("201"); var assigned = f.Flight("202", assigned: true);
        var perLanding = f.Flight("203", perLanding: true); var adHoc = f.Flight("204", adHoc: true);
        f.Db.Flights.AddRange(searched, assigned, perLanding, adHoc);
        f.Db.FlightUserActivities.AddRange(FlightUserActivity.Create(f.Actor.UserId, searched.Id, Now, true),
            FlightUserActivity.Create(f.Actor.UserId, assigned.Id, Now),
            FlightUserActivity.Create(f.Actor.UserId, perLanding.Id, Now, true),
            FlightUserActivity.Create(f.Actor.UserId, adHoc.Id, Now, true));
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var result = await f.HistoryAsync(new(UsedSearchOnly: true));
        result.Value.TotalCount.ShouldBe(1); result.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(searched.Id);
    }

    [Fact]
    public async Task History_text_filter_does_not_admit_viewed_only_or_another_accounts_matching_flights()
    {
        await using var f = new Fixture();
        var saved = f.Flight("301"); var viewed = f.Flight("302"); var otherAccount = f.Flight("303"); var unrelated = f.Flight("401");
        f.Db.Flights.AddRange(saved, viewed, otherAccount, unrelated);
        f.Db.FlightUserActivities.AddRange(FlightUserActivity.Create(f.Actor.UserId, saved.Id, Now),
            FlightUserActivity.Create(Guid.NewGuid(), otherAccount.Id, Now), FlightUserActivity.Create(f.Actor.UserId, unrelated.Id, Now));
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var result = await f.HistoryAsync(new(Search: "SV30"));
        result.Value.TotalCount.ShouldBe(1); result.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(saved.Id);
        (await f.HistoryAsync(new(Search: viewed.FlightNumber.Value))).Value.TotalCount.ShouldBe(0);
        (await f.Db.FlightUserActivities.AsNoTracking().CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task Inclusive_schedule_window_and_missing_station_metadata_follow_mobile_read_contract()
    {
        await using var f = new Fixture();
        var left = f.Flight("501", schedule: ScheduledTime.Create(Now.AddHours(-13), Now.AddHours(-12)).Value);
        var right = f.Flight("502", schedule: ScheduledTime.Create(Now.AddHours(12), Now.AddHours(13)).Value);
        f.Db.Flights.AddRange(left, right);
        f.Db.FlightUserActivities.AddRange(FlightUserActivity.Create(f.Actor.UserId, left.Id, Now), FlightUserActivity.Create(f.Actor.UserId, right.Id, Now));
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        (await f.HistoryAsync(new())).Value.TotalCount.ShouldBe(2);
        f.Reader.StationTimeZoneId = null;
        (await f.HistoryAsync(new())).Error.Code.ShouldBe("Operations.Flight.StationDataUnavailable");
    }

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

    private sealed class Fixture : IAsyncDisposable
    {
        public DbContextOptions<OperationsDbContext> Options { get; } = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public OperationsDbContext Db { get; }
        public SchedulingMasterDataReader Reader { get; } = new();
        public LogoReader Logos { get; } = new();
        public WorkUserNames Names { get; } = new();
        public MobileFlightScopeContext Actor { get; }
        private Guid Creator { get; } = Guid.NewGuid();
        private Guid Customer { get; } = Guid.NewGuid();

        public Fixture()
        {
            Db = new(Options); Actor = new(Reader.StationId, Guid.NewGuid(), Guid.NewGuid(), Now);
            Names.Values[Creator] = "Planning creator";
        }

        public Flight Flight(string number, bool assigned = false, bool perLanding = false, bool adHoc = false,
            Guid? id = null, Guid? stationId = null, ScheduledTime? schedule = null)
        {
            var customer = new CustomerSnapshot(Customer, "SV", "Customer");
            var station = new StationSnapshot(stationId ?? Reader.StationId, "RUH", "Station");
            schedule ??= ScheduledTime.Create(Now.AddHours(-1), Now.AddHours(1)).Value;
            return adHoc ? global::Operations.Domain.Flights.Flight.CreateAdHoc(customer, station,
                new(WellKnownMasterDataIds.AdHocOperationType, "AdHoc"), FlightNumber.CreateAdHoc(number).Value, schedule,
                Creator, Now, id ?? Guid.NewGuid(), new string('A', 64)).Value :
                global::Operations.Domain.Flights.Flight.ScheduleNew(customer, station, new(Guid.NewGuid(), "Transit"), FlightNumber.Create(number, "SV").Value,
                    schedule, null, [new(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : Guid.NewGuid(), "Service")],
                    assigned ? [new(Actor.StaffMemberId, "Assigned", "E1")] : [], Creator, Now, id).Value;
        }

        public Task<Result<Operations.Application.Contracts.MobileFlightPageDto>> HistoryAsync(GetMobileFlightHistoryQuery input) =>
            new GetMobileFlightHistoryQueryHandler(Db, new Scope(Actor), Logos, Reader, Names).Handle(input, CancellationToken.None);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Scope(MobileFlightScopeContext context) : IMobileFlightScope
    {
        public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(context));
    }

    private sealed class LogoReader : ICustomerLogoReader
    {
        public Task<CustomerLogoReadContent?> GetLogoAsync(Guid customerId, CancellationToken cancellationToken) => Task.FromResult<CustomerLogoReadContent?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> GetLogoVersionsAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(customerIds.ToDictionary(id => id, _ => "logo-v1"));
    }
}
