using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Authorization;
using MasterData.Application.Features.StaffMembers;
using MasterData.Domain.ManpowerTypes;
using MasterData.Domain.StaffMembers;
using MasterData.Domain.Stations;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MasterData.Application.UnitTests;

public sealed class StaffMemberCurrentStationOptionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Default_global_options_keep_the_portal_list_scope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Handler(null).Handle(new(), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        result.Value.StationId.ShouldBeNull();
        result.Value.Items.Select(s => s.StationId).Distinct().Count().ShouldBe(2);
        result.Value.Items.ShouldNotContain(s => s.Id == fixture.InactiveStaff.Id);
    }

    [Fact]
    public async Task Current_station_flag_restricts_even_global_readers_to_the_active_linked_station()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Handler(fixture.CurrentStaff.Id).Handle(new(CurrentStationOnly: true), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        result.Value.StationId.ShouldBe(fixture.FirstStation.Id);
        result.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(fixture.CurrentStaff.Id);
        result.Value.Items.ShouldAllBe(s => s.StationId == fixture.FirstStation.Id);
    }

    [Fact]
    public async Task A_server_side_staff_move_changes_the_derived_station_without_a_client_station_id()
    {
        await using var fixture = await Fixture.CreateAsync();
        var staff = fixture.CurrentStaff;
        staff.Update(staff.FullName, staff.EmployeeId, staff.Email, fixture.SecondStation.Id, staff.ManpowerTypeId,
            null, null, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Handler(staff.Id).Handle(new(CurrentStationOnly: true), CancellationToken.None);
        result.Value.StationId.ShouldBe(fixture.SecondStation.Id);
        result.Value.Items.Count.ShouldBe(2);
        result.Value.Items.ShouldAllBe(s => s.StationId == fixture.SecondStation.Id);
    }

    [Theory]
    [InlineData("unlinked")]
    [InlineData("missing-staff")]
    [InlineData("inactive-staff")]
    [InlineData("inactive-station")]
    public async Task Missing_or_inactive_linked_scope_returns_no_staff_or_station_metadata(string state)
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid? staffId = state == "unlinked" ? null : state == "missing-staff" ? Guid.NewGuid() : fixture.CurrentStaff.Id;
        if (state == "inactive-staff") fixture.CurrentStaff.Deactivate(Now).IsSuccess.ShouldBeTrue();
        if (state == "inactive-station") fixture.FirstStation.Deactivate(Now).IsSuccess.ShouldBeTrue();
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Handler(staffId).Handle(new(CurrentStationOnly: true), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldBeEmpty();
        result.Value.StationId.ShouldBeNull();
    }

    [Fact]
    public async Task A_caller_supplied_foreign_station_cannot_widen_current_station_options()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Handler(fixture.CurrentStaff.Id).Handle(new(fixture.SecondStation.Id, true), CancellationToken.None);
        result.Value.Items.ShouldBeEmpty();
        result.Value.StationId.ShouldBe(fixture.FirstStation.Id);
    }

    [Fact]
    public async Task Customer_scope_cannot_be_reinterpreted_as_a_staff_link()
    {
        await using var fixture = await Fixture.CreateAsync();
        var scope = new StaticScope(new(UserType.CustomerContact, null, Guid.NewGuid()));
        var handler = new GetActiveStaffMemberOptionsQueryHandler(fixture.Db, scope, new User(fixture.CurrentStaff.Id));
        var result = await handler.Handle(new(CurrentStationOnly: true), CancellationToken.None);
        result.Value.Items.ShouldBeEmpty();
        result.Value.StationId.ShouldBeNull();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required MasterDataDbContext Db { get; init; }
        public required Station FirstStation { get; init; }
        public required Station SecondStation { get; init; }
        public required StaffMember CurrentStaff { get; init; }
        public required StaffMember InactiveStaff { get; init; }
        public GetActiveStaffMemberOptionsQueryHandler Handler(Guid? link) => new(Db,
            new StaticScope(new(UserType.ViewerOnly, null, null)), new User(link));
        public static async Task<Fixture> CreateAsync()
        {
            var db = new MasterDataDbContext(new DbContextOptionsBuilder<MasterDataDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var first = Station.Create("RUH", null, "Riyadh", null, Guid.NewGuid(), Now, timeZoneId: "Asia/Riyadh").Value;
            var second = Station.Create("JED", null, "Jeddah", null, Guid.NewGuid(), Now, timeZoneId: "Asia/Riyadh").Value;
            var manpower = ManpowerType.Create("Engineer", null, Now).Value;
            StaffMember Staff(string employeeId, Guid station) => StaffMember.Create("Employee " + employeeId, employeeId,
                employeeId + "@example.com", station, manpower.Id, null, null, Now).Value;
            var current = Staff("E1", first.Id);
            var inactive = Staff("E2", first.Id);
            inactive.Deactivate(Now).IsSuccess.ShouldBeTrue();
            db.Stations.AddRange(first, second);
            db.ManpowerTypes.Add(manpower);
            db.StaffMembers.AddRange(current, inactive, Staff("E3", second.Id));
            await db.SaveChangesAsync();
            return new Fixture { Db = db, FirstStation = first, SecondStation = second, CurrentStaff = current, InactiveStaff = inactive };
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class StaticScope(MasterDataScopeContext context) : IMasterDataScope
    {
        public Task<Result<MasterDataScopeContext>> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(context));
    }
    private sealed class User(Guid? staffId) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.ViewerOnly;
        public Guid? ExternalReferenceId => staffId;
        public bool HasPermission(string permission) => true;
    }
}
