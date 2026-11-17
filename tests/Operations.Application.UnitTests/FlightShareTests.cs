using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
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

public sealed class FlightShareTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Replays_are_normalized_immutable_and_do_not_change_operational_state()
    {
        await using var f = new Fixture(); var flight = await f.Seed(); var id = Guid.NewGuid();
        var before = await f.Detail(flight.Id, f.AuthorStaff, f.Author);
        var published = await f.Publish(flight.Id, id, new("  Handover  ", [f.RecipientStaff, f.RecipientStaff]));
        published.IsSuccess.ShouldBeTrue(); published.Value.Message.ShouldBe("Handover");
        published.Value.Recipients.ShouldHaveSingleItem().FullName.ShouldBe("Recipient");
        var replay = await f.Publish(flight.Id, id, new("Handover", [f.RecipientStaff]));
        replay.IsSuccess.ShouldBeTrue(); replay.Value.Id.ShouldBe(id); replay.Value.CreatedAtUtc.ShouldBe(Now);
        (await f.Publish(flight.Id, id, new("Changed", [f.RecipientStaff]))).Error.Code.ShouldBe("Operations.FlightShare.IdConflict");
        (await f.Publish(flight.Id, id, new("Handover", [f.RecipientStaff]), f.RecipientStaff, f.Recipient)).Error.Code.ShouldBe("Operations.FlightShare.IdConflict");
        var after = await f.Detail(flight.Id, f.AuthorStaff, f.Author);
        JsonSerializer.Serialize(after.Value).ShouldBe(JsonSerializer.Serialize(before.Value));
        (await f.Db.FlightShares.CountAsync()).ShouldBe(1); (await f.Db.FlightUserActivities.CountAsync()).ShouldBe(0);
        (await f.Db.OutboxMessages.CountAsync()).ShouldBe(0);
        f.Db.ChangeTracker.Entries<Flight>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_share_is_a_post_and_received_and_sent_keep_their_distinct_audiences()
    {
        await using var f = new Fixture(); var flight = await f.Seed();
        foreach (var input in new[] { new PublishFlightShareRequest("Public"), new("Target", [f.RecipientStaff]), new("Other", [f.OtherStaff]), new("Public") })
            (await f.Publish(flight.Id, Guid.NewGuid(), input)).IsSuccess.ShouldBeTrue();
        var received = await f.Feed(f.RecipientStaff, f.Recipient);
        received.Value.Items.Count.ShouldBe(3); received.Value.Items.Count(item => item.IsStationWide).ShouldBe(2);
        received.Value.Items.ShouldAllBe(item => item.Flight.IsMyFlight == false && item.Flight.Id == flight.Id);
        var sent = await f.Feed(f.AuthorStaff, f.Author, "sent"); sent.Value.TotalCount.ShouldBe(4);
        (await f.Feed(f.AuthorStaff, f.Author)).Value.Items.ShouldBeEmpty();
        var first = await f.Feed(f.RecipientStaff, f.Recipient, page: 1, pageSize: 2);
        var second = await f.Feed(f.RecipientStaff, f.Recipient, page: 2, pageSize: 2);
        first.Value.Items.Select(item => item.Id).Concat(second.Value.Items.Select(item => item.Id)).Distinct().Count().ShouldBe(3);
        first.Value.HasNextPage.ShouldBeTrue(); second.Value.HasNextPage.ShouldBeFalse();
        (await f.Feed(f.RecipientStaff, f.Recipient, search: "SV100")).Value.TotalCount.ShouldBe(3);
        (await f.Feed(f.RecipientStaff, f.Recipient, search: "SV999")).Value.Items.ShouldBeEmpty();
        (await f.Feed(f.RecipientStaff, f.Recipient, "unknown")).Error.Code.ShouldBe("Operations.FlightShare.DirectionInvalid");
    }

    [Fact]
    public async Task Sharing_never_changes_search_permissions_or_adds_to_the_assigned_feed()
    {
        await using var f = new Fixture(); var flight = await f.Seed(withArrival: true);
        var before = await f.Detail(flight.Id, f.RecipientStaff, f.Recipient);
        before.Value.IsUnassignedSearchFlight.ShouldBeTrue(); before.Value.CanAddServices.ShouldBe(false);
        before.Value.CanSaveArrival.ShouldBeFalse(); before.Value.CanShare.ShouldBeFalse();
        (await f.Publish(flight.Id, Guid.NewGuid(), new("Continue work", [f.RecipientStaff]))).IsSuccess.ShouldBeTrue();
        var after = await f.Detail(flight.Id, f.RecipientStaff, f.Recipient);
        JsonSerializer.Serialize(after.Value).ShouldBe(JsonSerializer.Serialize(before.Value));
        var normal = await new GetMobileFlightsQueryHandler(f.Db, f.Scope(f.RecipientStaff, f.Recipient), NoopCustomerLogoReader.Instance, f.Reader)
            .Handle(new(Category: "MyFlights"), default);
        normal.Value.Items.ShouldBeEmpty(); after.Value.AssignedEmployees.ShouldHaveSingleItem().StaffMemberId.ShouldBe(f.AuthorStaff);
    }

    [Fact]
    public async Task Combined_default_feed_globally_pages_own_public_and_targeted_posts_without_private_posts_or_duplicates()
    {
        await using var f = new Fixture(); var flight = await f.Seed();
        var tracked = await f.Db.Flights.Include(item => item.AssignedEmployees).SingleAsync();
        tracked.ReplaceAssignedEmployees([new(f.AuthorStaff, "Author", "E1"), new(f.RecipientStaff, "Recipient", "E2")], Now).IsSuccess.ShouldBeTrue();
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var ownPrivate = Guid.NewGuid(); var ownPublic = Guid.NewGuid(); var publicPost = Guid.NewGuid(); var targeted = Guid.NewGuid();
        (await f.Publish(flight.Id, ownPrivate, new("Own private", [f.OtherStaff]))).IsSuccess.ShouldBeTrue();
        (await f.Publish(flight.Id, publicPost, new("Public"), f.RecipientStaff, f.Recipient)).IsSuccess.ShouldBeTrue();
        (await f.Publish(flight.Id, targeted, new("For author", [f.AuthorStaff]), f.RecipientStaff, f.Recipient)).IsSuccess.ShouldBeTrue();
        (await f.Publish(flight.Id, Guid.NewGuid(), new("Not for author", [f.OtherStaff]), f.RecipientStaff, f.Recipient)).IsSuccess.ShouldBeTrue();
        (await f.Publish(flight.Id, ownPublic, new("Own public"))).IsSuccess.ShouldBeTrue();
        var handler = new GetMobileFlightSharesQueryHandler(f.Db, f.Scope(f.AuthorStaff, f.Author), f.Reader, NoopCustomerLogoReader.Instance);
        var combined = await handler.Handle(new(), default);
        var expected = new[] { ownPrivate, ownPublic, publicPost, targeted }.Order().ToArray();
        combined.Value.Items.Select(item => item.Id).ShouldBe(expected); combined.Value.TotalCount.ShouldBe(4);
        combined.Value.Items.ShouldAllBe(item => item.StationName == "Riyadh");
        var first = await f.Feed(f.AuthorStaff, f.Author, "all", pageSize: 2);
        var second = await f.Feed(f.AuthorStaff, f.Author, "all", page: 2, pageSize: 2);
        first.Value.Items.Concat(second.Value.Items).Select(item => item.Id).ShouldBe(expected);
        (await f.Feed(f.AuthorStaff, f.Author)).Value.TotalCount.ShouldBe(2);
        (await f.Feed(f.AuthorStaff, f.Author, "sent")).Value.TotalCount.ShouldBe(2);
    }

    [Theory]
    [InlineData("unassigned", "Operations.FlightShare.AssignmentRequired")]
    [InlineData("canceled", "Operations.FlightShare.Canceled")]
    [InlineData("too-early", "Operations.Flight.NotFound")]
    [InlineData("wrong-station", "Operations.Flight.NotFound")]
    [InlineData("ad-hoc", "Operations.Flight.NotFound")]
    [InlineData("landing", "Operations.Flight.NotFound")]
    public async Task Publishers_cannot_widen_assignment_station_type_or_lifecycle_eligibility(string kind, string expected)
    {
        await using var f = new Fixture(); var flight = await f.Seed(kind: kind);
        var result = await f.Publish(flight.Id, Guid.NewGuid(), new(), kind == "unassigned" ? f.RecipientStaff : f.AuthorStaff,
            kind == "unassigned" ? f.Recipient : f.Author);
        result.Error.Code.ShouldBe(expected); (await f.Db.FlightShares.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_message_recipients_and_inactive_or_moved_staff_are_rejected()
    {
        await using var f = new Fixture(); var flight = await f.Seed();
        (await f.Publish(flight.Id, Guid.NewGuid(), new(new string('x', 2001)))).Error.Code.ShouldBe("Operations.FlightShare.Invalid");
        (await f.Publish(flight.Id, Guid.NewGuid(), new(null, [Guid.Empty]))).Error.Code.ShouldBe("Operations.FlightShare.Invalid");
        (await f.Publish(flight.Id, Guid.NewGuid(), new(null, [Guid.NewGuid()]))).Error.Code.ShouldBe("Operations.Ref.StaffNotFound");
        f.Reader.StaffMembers[f.RecipientStaff] = new(f.RecipientStaff, "Inactive", "E2", f.Reader.StationId, f.Recipient, false);
        (await f.Publish(flight.Id, Guid.NewGuid(), new(null, [f.RecipientStaff]))).Error.Code.ShouldBe("Operations.Ref.StaffInactive");
        f.Reader.StaffMembers[f.RecipientStaff] = new(f.RecipientStaff, "Moved", "E2", Guid.NewGuid(), f.Recipient, true);
        (await f.Publish(flight.Id, Guid.NewGuid(), new(null, [f.RecipientStaff]))).Error.Code.ShouldBe("Operations.Ref.StaffStationMismatch");
        (await f.Db.FlightShares.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Accepted_retry_recovers_after_cancellation_but_new_posts_are_denied()
    {
        await using var f = new Fixture(); var flight = await f.Seed(); var id = Guid.NewGuid();
        (await f.Publish(flight.Id, id, new())).IsSuccess.ShouldBeTrue();
        var tracked = await f.Db.Flights.SingleAsync();
        tracked.SubmitCancellation(new(f.AuthorStaff, "Author", "E1"), CancellationDetails.Create(Now, null).Value, f.Author, Now).IsSuccess.ShouldBeTrue();
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        (await f.Publish(flight.Id, id, new())).IsSuccess.ShouldBeTrue();
        (await f.Publish(flight.Id, Guid.NewGuid(), new())).Error.Code.ShouldBe("Operations.FlightShare.Canceled");
        (await f.Detail(flight.Id, f.AuthorStaff, f.Author)).Value.CanShare.ShouldBeFalse();
        (await f.Feed(f.RecipientStaff, f.Recipient)).Value.TotalCount.ShouldBe(1);
        (await f.Feed(f.RecipientStaff, f.Recipient, now: Now.AddHours(12).AddTicks(1))).Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Station_scope_and_current_assignment_do_not_leak_posts_or_mutate_the_original_snapshots()
    {
        await using var f = new Fixture(); var flight = await f.Seed();
        (await f.Publish(flight.Id, Guid.NewGuid(), new("Note", [f.RecipientStaff]))).IsSuccess.ShouldBeTrue();
        f.Reader.StaffMembers[f.RecipientStaff] = new(f.RecipientStaff, "Renamed", "E2", f.Reader.StationId, f.Recipient, true);
        var received = await f.Feed(f.RecipientStaff, f.Recipient);
        received.Value.Items.Single().Recipients.Single().FullName.ShouldBe("Recipient");
        (await f.Feed(f.RecipientStaff, f.Recipient, station: Guid.NewGuid())).Value.Items.ShouldBeEmpty();
        (await f.Detail(flight.Id, f.AuthorStaff, f.Author)).Value.CanShare.ShouldBeTrue();
        (await f.Detail(flight.Id, f.AuthorStaff, f.Author, operate: false)).Value.CanShare.ShouldBeFalse();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public OperationsDbContext Db { get; } = new(new DbContextOptionsBuilder<OperationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public SchedulingMasterDataReader Reader { get; } = new();
        public Guid AuthorStaff { get; } = Guid.NewGuid(); public Guid Author { get; } = Guid.NewGuid();
        public Guid RecipientStaff { get; } = Guid.NewGuid(); public Guid Recipient { get; } = Guid.NewGuid();
        public Guid OtherStaff { get; } = Guid.NewGuid();
        public Fixture()
        {
            Reader.StaffMembers[AuthorStaff] = new(AuthorStaff, "Author", "E1", Reader.StationId, Author, true);
            Reader.StaffMembers[RecipientStaff] = new(RecipientStaff, "Recipient", "E2", Reader.StationId, Recipient, true);
            Reader.StaffMembers[OtherStaff] = new(OtherStaff, "Other", "E3", Reader.StationId, Guid.NewGuid(), true);
        }
        public IMobileFlightScope Scope(Guid staff, Guid user, Guid? station = null, DateTimeOffset? now = null) => new ScopeImpl(new(station ?? Reader.StationId, staff, user, now ?? Now));
        public async Task<Flight> Seed(bool withArrival = false, string? kind = null)
        {
            var employee = new StaffMemberSnapshot(AuthorStaff, "Author", "E1");
            var flight = Flight.ScheduleNew(new(Guid.NewGuid(), "SV", "Airline"), new(kind == "wrong-station" ? Guid.NewGuid() : Reader.StationId, "RUH", "Riyadh"),
                new(kind == "ad-hoc" ? WellKnownMasterDataIds.AdHocOperationType : Guid.NewGuid(), "Operation"), FlightNumber.Create("SV100", "SV").Value,
                ScheduledTime.Create(Now.AddHours(kind == "too-early" ? 13 : -100), Now.AddHours(kind == "too-early" ? 14 : -99)).Value, null,
                [new(kind == "landing" ? WellKnownMasterDataIds.AircraftPerLandingService : Reader.ServiceId, "Service")],
                kind is "ad-hoc" or "landing" ? [] : [employee], Author, Now).Value;
            if (withArrival) flight.SubmitArrival(employee, ArrivalDetails.Create(null, Now.AddHours(-98),
                new(Guid.NewGuid(), "Airbus", "A320"), "HZ-TEST", employee, null).Value, Author, Now).IsSuccess.ShouldBeTrue();
            if (kind == "canceled") flight.SubmitCancellation(employee, CancellationDetails.Create(Now, null).Value, Author, Now).IsSuccess.ShouldBeTrue();
            Db.Flights.Add(flight); Db.Entry(flight).Property(item => item.RowVersion).CurrentValue = new byte[8];
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear(); return flight;
        }
        public Task<Result<FlightSharePostDto>> Publish(Guid flight, Guid id, PublishFlightShareRequest input, Guid? staff = null, Guid? user = null) =>
            new PublishFlightShareCommandHandler(Db, Scope(staff ?? AuthorStaff, user ?? Author), new(Reader), Reader, NoopCustomerLogoReader.Instance, new Clock())
                .Handle(new(flight, id, input), default);
        public Task<Result<MobileFlightSharePageDto>> Feed(Guid staff, Guid user, string direction = "received", int page = 1, int pageSize = 20,
            string? search = null, Guid? station = null, DateTimeOffset? now = null) =>
            new GetMobileFlightSharesQueryHandler(Db, Scope(staff, user, station, now), Reader, NoopCustomerLogoReader.Instance)
                .Handle(new(direction, search, page, pageSize), default);
        public Task<Result<FlightDetailDto>> Detail(Guid flight, Guid staff, Guid user, bool operate = true) =>
            new GetMobileFlightByIdQueryHandler(Db, Scope(staff, user), Reader, new User(user, operate)).Handle(new(flight), default);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class ScopeImpl(MobileFlightScopeContext actor) : IMobileFlightScope
    { public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken ct) => Task.FromResult(Result.Success(actor)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class User(Guid id, bool operate) : IUserContext
    {
        public bool IsAuthenticated => true; public Guid? UserId => id;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.StationStaff;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => operate && permission == OperationsPermissions.Flights.Operate;
    }
}
