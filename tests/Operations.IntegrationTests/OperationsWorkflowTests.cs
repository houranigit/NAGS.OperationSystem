using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using BuildingBlocks.Application.Mobile;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OperationsSystem.Api.Mobile;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

/// <summary>
/// End-to-end Operations workflows through the real API: scheduling rules, bulk scheduling,
/// Per-Landing vs assigned visibility, and roster updates.
/// </summary>
public sealed class OperationsWorkflowTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    private const string Base = OperationsApiFactory.Base;
    private const string MasterDataBase = OperationsApiFactory.MasterDataBase;
    private const string IdentityBase = OperationsApiFactory.IdentityBase;

    private static int _stationCounter;

    private static readonly string[] StaffOperationsPermissions =
    [
        "operations.flights.view",
        "operations.flights.assign"
    ];

    [Fact]
    public async Task Schedule_without_planned_services_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);

        var response = await admin.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId,
            stationId = refs.StationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber = "1100",
            scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(2),
            scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(4),
            aircraftTypeId = (Guid?)null,
            plannedServiceIds = Array.Empty<Guid>(),
            assignedStaffMemberIds = Array.Empty<Guid>()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Schedule_with_per_landing_mixed_with_other_service_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);

        var response = await admin.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId,
            stationId = refs.StationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber = "1101",
            scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(2),
            scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(4),
            aircraftTypeId = (Guid?)null,
            plannedServiceIds = new[] { WellKnownMasterDataIds.AircraftPerLandingService, refs.ServiceId },
            assignedStaffMemberIds = Array.Empty<Guid>()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Schedule_with_per_landing_assigned_staff_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);

        var response = await admin.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId,
            stationId = refs.StationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber = "1102",
            scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(2),
            scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(4),
            aircraftTypeId = (Guid?)null,
            plannedServiceIds = new[] { WellKnownMasterDataIds.AircraftPerLandingService },
            assignedStaffMemberIds = new[] { refs.StaffMemberId }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bulk_schedule_creates_one_flight_per_selected_date_with_shared_details()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (_, assignedStaffId) = await CreateStaffLoginAsync(admin, refs);
        var selectedDates = new[]
        {
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 5),
            new DateOnly(2026, 8, 8)
        };

        var response = await admin.PostAsJsonAsync($"{Base}/flights/bulk", new
        {
            customerId = refs.CustomerId,
            stationId = refs.StationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber = "1150",
            scheduledArrivalTimeUtc = new TimeOnly(23, 30),
            scheduledDepartureTimeUtc = new TimeOnly(1, 15),
            selectedDates,
            aircraftTypeId = refs.AircraftTypeId,
            plannedServiceIds = new[] { refs.ServiceId },
            assignedStaffMemberIds = new[] { assignedStaffId }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var ids = await response.Content.ReadFromJsonAsync<List<Guid>>();
        ids.ShouldNotBeNull();
        ids!.Count.ShouldBe(selectedDates.Length);

        for (var i = 0; i < ids.Count; i++)
        {
            var flight = await GetFlightAsync(admin, ids[i]);
            var expectedArrival = new DateTimeOffset(selectedDates[i].ToDateTime(new TimeOnly(23, 30), DateTimeKind.Utc));
            var expectedDeparture = new DateTimeOffset(selectedDates[i].AddDays(1).ToDateTime(new TimeOnly(1, 15), DateTimeKind.Utc));

            flight.FlightNumber.ShouldBe("1150");
            flight.ScheduledArrivalUtc.ShouldBe(expectedArrival);
            flight.ScheduledDepartureUtc.ShouldBe(expectedDeparture);
            flight.PlannedServices.ShouldContain(service => service.ServiceId == refs.ServiceId);
            flight.AssignedEmployees.ShouldContain(employee => employee.StaffMemberId == assignedStaffId);
        }
    }

    [Fact]
    public async Task Flight_number_update_rejects_stale_version_without_overwriting_current_number()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var flightId = await ScheduleFlightAsync(admin, refs, "1160");
        var flight = await GetFlightAsync(admin, flightId);

        using var first = new HttpRequestMessage(HttpMethod.Post, $"{Base}/flights/{flightId}/change-number")
        {
            Content = JsonContent.Create(new { flightNumber = "1161" })
        };
        first.Headers.TryAddWithoutValidation("If-Match", flight.RowVersion);
        (await admin.SendAsync(first)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stale = new HttpRequestMessage(HttpMethod.Post, $"{Base}/flights/{flightId}/change-number")
        {
            Content = JsonContent.Create(new { flightNumber = "1162" })
        };
        stale.Headers.TryAddWithoutValidation("If-Match", flight.RowVersion);
        (await admin.SendAsync(stale)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await GetFlightAsync(admin, flightId)).FlightNumber.ShouldBe("1161");
    }

    [Fact]
    public async Task Non_per_landing_flight_is_visible_only_to_assigned_staff()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (assignedClient, assignedStaffId) = await CreateStaffLoginAsync(admin, refs);
        var (unassignedClient, _) = await CreateStaffLoginAsync(admin, refs);

        var flightId = await ScheduleFlightAsync(admin, refs, "1400", assignedStaffIds: [assignedStaffId]);

        (await assignedClient.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await unassignedClient.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var assignedList = await assignedClient.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        assignedList!.Items.ShouldContain(f => f.Id == flightId);
        var unassignedList = await unassignedClient.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        unassignedList!.Items.ShouldNotContain(f => f.Id == flightId);
    }

    [Fact]
    public async Task Per_landing_flight_is_visible_station_wide()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (staffClient, _) = await CreateStaffLoginAsync(admin, refs);

        var flightId = await ScheduleFlightAsync(admin, refs, "1401",
            plannedServiceIds: [WellKnownMasterDataIds.AircraftPerLandingService]);

        (await staffClient.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await staffClient.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        list!.Items.ShouldContain(f => f.Id == flightId && f.IsPerLanding);
    }

    [Fact]
    public async Task Assigning_staff_to_per_landing_flight_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var flightId = await ScheduleFlightAsync(admin, refs, "1402",
            plannedServiceIds: [WellKnownMasterDataIds.AircraftPerLandingService]);
        var flight = await GetFlightAsync(admin, flightId);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/flights/{flightId}/assign")
        {
            Content = JsonContent.Create(new { staffMemberIds = new[] { refs.StaffMemberId } })
        };
        request.Headers.TryAddWithoutValidation("If-Match", flight.RowVersion);

        var response = await admin.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Assign_endpoint_replaces_and_clears_the_scheduled_flight_roster()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (_, replacementStaffId) = await CreateStaffLoginAsync(admin, refs);
        var flightId = await ScheduleFlightAsync(admin, refs, "1403", assignedStaffIds: [refs.StaffMemberId]);
        var flight = await GetFlightAsync(admin, flightId);

        using var replaceRequest = new HttpRequestMessage(HttpMethod.Post, $"{Base}/flights/{flightId}/assign")
        {
            Content = JsonContent.Create(new { staffMemberIds = new[] { replacementStaffId } })
        };
        replaceRequest.Headers.TryAddWithoutValidation("If-Match", flight.RowVersion);

        var replaceResponse = await admin.SendAsync(replaceRequest);
        replaceResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await replaceResponse.Content.ReadAsStringAsync());

        var replaced = await GetFlightAsync(admin, flightId);
        replaced.AssignedEmployees.ShouldContain(e => e.StaffMemberId == replacementStaffId);
        replaced.AssignedEmployees.ShouldNotContain(e => e.StaffMemberId == refs.StaffMemberId);
        using var clearRequest = new HttpRequestMessage(HttpMethod.Post, $"{Base}/flights/{flightId}/assign")
        {
            Content = JsonContent.Create(new { staffMemberIds = Array.Empty<Guid>() })
        };
        clearRequest.Headers.TryAddWithoutValidation("If-Match", replaced.RowVersion);

        var clearResponse = await admin.SendAsync(clearRequest);
        clearResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await clearResponse.Content.ReadAsStringAsync());
        (await GetFlightAsync(admin, flightId)).AssignedEmployees.ShouldBeEmpty();
    }

    [Fact]
    public async Task Schedule_edit_can_clear_roster_and_change_to_per_landing_in_one_request()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var flightId = await ScheduleFlightAsync(admin, refs, "1404", assignedStaffIds: [refs.StaffMemberId]);
        var flight = await GetFlightAsync(admin, flightId);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{flightId}")
        {
            Content = JsonContent.Create(new
            {
                customerId = refs.CustomerId,
                stationId = refs.StationId,
                operationTypeId = refs.OperationTypeId,
                scheduledArrivalUtc = flight.ScheduledArrivalUtc,
                scheduledDepartureUtc = flight.ScheduledDepartureUtc,
                aircraftTypeId = (Guid?)null,
                plannedServiceIds = new[] { WellKnownMasterDataIds.AircraftPerLandingService },
                assignedStaffMemberIds = Array.Empty<Guid>()
            })
        };
        request.Headers.TryAddWithoutValidation("If-Match", flight.RowVersion);

        var response = await admin.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var updated = await GetFlightAsync(admin, flightId);
        updated.IsPerLanding.ShouldBeTrue();
        updated.AssignedEmployees.ShouldBeEmpty();
        updated.PlannedServices.ShouldHaveSingleItem().ServiceId
            .ShouldBe(WellKnownMasterDataIds.AircraftPerLandingService);
    }

    [Fact]
    public async Task Invite_permission_adds_employees_once_without_replacing_roster_and_notifies_the_invitee()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var roleId = await CreateInviteRoleAsync(admin);
        var (inviter, inviterId) = await CreateStaffLoginAsync(admin, refs, roleId);
        var (invitee, inviteeId) = await CreateStaffLoginAsync(admin, refs);
        var flightId = await ScheduleFlightAsync(admin, refs, "2100", assignedStaffIds: [inviterId]);
        var flight = await GetFlightAsync(inviter, flightId);

        var options = await inviter.GetFromJsonAsync<List<AssignedEmployee>>($"{Base}/flights/{flightId}/invitation-options");
        options!.ShouldContain(employee => employee.StaffMemberId == inviteeId);
        options!.ShouldNotContain(employee => employee.StaffMemberId == inviterId);

        using var request = InvitationRequest(flightId, [inviterId, inviteeId, inviteeId], flight.RowVersion);
        var response = await inviter.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var updated = await GetFlightAsync(admin, flightId);
        updated.AssignedEmployees.Select(employee => employee.StaffMemberId).Order()
            .ShouldBe(new[] { inviterId, inviteeId }.Order());
        using var repeated = InvitationRequest(flightId, [inviteeId], updated.RowVersion);
        (await inviter.SendAsync(repeated)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetFlightAsync(admin, flightId)).AssignedEmployees.Count.ShouldBe(2);

        await factory.DrainOutboxesAsync();
        (await invitee.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var inbox = await invitee.GetFromJsonAsync<NotificationPage>("/api/v1/notifications/me?page=1&pageSize=20");
        inbox!.Items.Count(item => item.Kind == "EmployeeInvitedToFlight" && item.Payload.GetValueOrDefault("flightId") == flightId.ToString())
            .ShouldBe(1);
        options = await inviter.GetFromJsonAsync<List<AssignedEmployee>>($"{Base}/flights/{flightId}/invitation-options");
        options!.ShouldNotContain(employee => employee.StaffMemberId == inviteeId);
    }

    [Fact]
    public async Task Invitation_routes_require_invite_permission_even_when_the_flight_is_accessible()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (staff, staffId) = await CreateStaffLoginAsync(admin, refs);
        var flightId = await ScheduleFlightAsync(admin, refs, "2200", assignedStaffIds: [staffId]);
        var flight = await GetFlightAsync(staff, flightId);

        (await staff.GetAsync($"{Base}/flights/{flightId}/invitation-options")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var request = InvitationRequest(flightId, [refs.StaffMemberId], flight.RowVersion);
        (await staff.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invite_permission_does_not_bypass_flight_scope(bool otherStation)
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var roleId = await CreateInviteRoleAsync(admin);
        var (inviter, _) = await CreateStaffLoginAsync(admin, refs, roleId);
        var flightRefs = otherStation ? await SetupMasterDataAsync(admin) : refs;
        var flightId = await ScheduleFlightAsync(admin, flightRefs, "2300");
        var flight = await GetFlightAsync(admin, flightId);

        (await inviter.GetAsync($"{Base}/flights/{flightId}/invitation-options")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var request = InvitationRequest(flightId, [flightRefs.StaffMemberId], flight.RowVersion);
        (await inviter.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetFlightAsync(admin, flightId)).AssignedEmployees.ShouldBeEmpty();
    }

    [Fact]
    public async Task Per_landing_flights_do_not_offer_employee_invitations()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var flightId = await ScheduleFlightAsync(admin, refs, "2400", [WellKnownMasterDataIds.AircraftPerLandingService]);
        var flight = await GetFlightAsync(admin, flightId);

        (await admin.GetAsync($"{Base}/flights/{flightId}/invitation-options")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var request = InvitationRequest(flightId, [refs.StaffMemberId], flight.RowVersion);
        (await admin.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetFlightAsync(admin, flightId)).AssignedEmployees.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invitation_requires_current_version_and_stale_write_preserves_roster_and_outbox()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (_, secondStaffId) = await CreateStaffLoginAsync(admin, refs);
        var flightId = await ScheduleFlightAsync(admin, refs, "2500");
        var flight = await GetFlightAsync(admin, flightId);

        using var missingVersion = InvitationRequest(flightId, [refs.StaffMemberId]);
        (await admin.SendAsync(missingVersion)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetFlightAsync(admin, flightId)).AssignedEmployees.ShouldBeEmpty();
        using var accepted = InvitationRequest(flightId, [refs.StaffMemberId], flight.RowVersion);
        (await admin.SendAsync(accepted)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var stale = InvitationRequest(flightId, [secondStaffId], flight.RowVersion);
        (await admin.SendAsync(stale)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var saved = await GetFlightAsync(admin, flightId);
        saved.AssignedEmployees.ShouldHaveSingleItem().StaffMemberId.ShouldBe(refs.StaffMemberId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        (await db.OutboxMessages.CountAsync(message =>
            message.Type.StartsWith("Operations.Contracts.FlightEmployeeAssigned") && message.Content.Contains(flightId.ToString())))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Viewer_can_read_flights_but_cannot_read_invitation_options_or_invite()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var flightId = await ScheduleFlightAsync(admin, refs, "2600");
        var flight = await GetFlightAsync(admin, flightId);
        var roleId = await PostForIdAsync(admin, $"{IdentityBase}/roles", new
        {
            name = $"Ops Viewer {Guid.NewGuid():N}", description = (string?)null,
            compatibleUserType = "ViewerOnly", permissions = new[] { "operations.flights.view" }
        });
        var email = $"viewer-{Guid.NewGuid():N}@example.com";
        var invited = await admin.PostAsJsonAsync($"{IdentityBase}/users/invite", new { email, displayName = "Operations viewer", roleId });
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync());
        var invitationToken = await factory.GetInvitationTokenAsync(email);
        const string password = "ViewerPass#12345";
        (await admin.PostAsJsonAsync($"{IdentityBase}/auth/activate", new { email, invitationToken, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var viewer = await factory.CreateAuthenticatedClientAsync(email, password);

        (await viewer.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"{Base}/flights/{flightId}/invitation-options")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var request = InvitationRequest(flightId, [refs.StaffMemberId], flight.RowVersion);
        (await viewer.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Flight_sync_schedule_and_batch_publish_one_scoped_refresh_after_commit()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var otherRefs = await SetupMasterDataAsync(admin);
        var (own, ownStaffId) = await CreateStaffLoginAsync(admin, refs);
        var (other, _) = await CreateStaffLoginAsync(admin, otherRefs);
        await using var adminLive = await CatalogLiveSubscription.ConnectAsync(factory, admin);
        await using var ownLive = await CatalogLiveSubscription.ConnectAsync(factory, own);
        await using var otherLive = await CatalogLiveSubscription.ConnectAsync(factory, other);
        async Task ExpectFlight() => await Task.WhenAll(adminLive.ExpectRefreshAsync("flights"),
            ownLive.ExpectRefreshAsync("flights"), otherLive.ExpectRefreshAsync("flights"));
        async Task ExpectNone() => await Task.WhenAll(adminLive.ExpectNoRefreshAsync(),
            ownLive.ExpectNoRefreshAsync(), otherLive.ExpectNoRefreshAsync());

        var id = await ScheduleFlightAsync(admin, refs, "3901", assignedStaffIds: [ownStaffId]);
        await ExpectFlight();
        (await FlightIdsAsync(admin)).ShouldContain(id);
        (await FlightIdsAsync(own)).ShouldContain(id);
        (await FlightIdsAsync(other)).ShouldNotContain(id);
        await ExpectNone();

        var invalid = await admin.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId, stationId = refs.StationId, operationTypeId = refs.OperationTypeId,
            flightNumber = "3902", scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(2),
            scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(4), aircraftTypeId = (Guid?)null,
            plannedServiceIds = Array.Empty<Guid>(), assignedStaffMemberIds = Array.Empty<Guid>()
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ExpectNone();

        var dates = new[] { new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 10) };
        var bulk = await admin.PostAsJsonAsync($"{Base}/flights/bulk", new
        {
            customerId = refs.CustomerId, stationId = refs.StationId, operationTypeId = refs.OperationTypeId,
            flightNumber = "3903", scheduledArrivalTimeUtc = new TimeOnly(10, 0),
            scheduledDepartureTimeUtc = new TimeOnly(12, 0), selectedDates = dates,
            aircraftTypeId = (Guid?)null, plannedServiceIds = new[] { refs.ServiceId },
            assignedStaffMemberIds = new[] { ownStaffId }
        });
        bulk.StatusCode.ShouldBe(HttpStatusCode.OK, await bulk.Content.ReadAsStringAsync());
        var ids = (await bulk.Content.ReadFromJsonAsync<List<Guid>>())!;
        ids.Count.ShouldBe(2);
        await ExpectFlight();
        var ownFlights = await FlightIdsAsync(own);
        var otherFlights = await FlightIdsAsync(other);
        foreach (var flightId in ids)
        {
            ownFlights.ShouldContain(flightId);
            otherFlights.ShouldNotContain(flightId);
            (await GetFlightAsync(admin, flightId)).FlightNumber.ShouldBe("3903");
        }
        // A batch is one invalidation, never one event per created row or another dataset.
        await ExpectNone();
    }

    [Fact]
    public async Task Flight_sync_updates_numbers_rosters_and_invitations_without_failure_or_noop_hints()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var otherRefs = await SetupMasterDataAsync(admin);
        var inviteRole = await CreateInviteRoleAsync(admin);
        var (first, firstStaffId) = await CreateStaffLoginAsync(admin, refs, inviteRole);
        var (second, secondStaffId) = await CreateStaffLoginAsync(admin, refs, inviteRole);
        var (other, otherStaffId) = await CreateStaffLoginAsync(admin, otherRefs);
        var id = await ScheduleFlightAsync(admin, refs, "3910", assignedStaffIds: [firstStaffId]);
        var original = await GetFlightAsync(admin, id);
        await using var adminLive = await CatalogLiveSubscription.ConnectAsync(factory, admin);
        await using var firstLive = await CatalogLiveSubscription.ConnectAsync(factory, first);
        await using var secondLive = await CatalogLiveSubscription.ConnectAsync(factory, second);
        await using var otherLive = await CatalogLiveSubscription.ConnectAsync(factory, other);
        async Task ExpectFlight() => await Task.WhenAll(adminLive.ExpectRefreshAsync("flights"),
            firstLive.ExpectRefreshAsync("flights"), secondLive.ExpectRefreshAsync("flights"), otherLive.ExpectRefreshAsync("flights"));
        async Task ExpectNone() => await Task.WhenAll(adminLive.ExpectNoRefreshAsync(),
            firstLive.ExpectNoRefreshAsync(), secondLive.ExpectNoRefreshAsync(), otherLive.ExpectNoRefreshAsync());

        var arrival = original.ScheduledArrivalUtc.AddHours(1);
        IReadOnlyList<Guid> services = [refs.ServiceId];
        IReadOnlyList<Guid> roster = [firstStaffId];
        object PlanningBody() => new
        {
            customerId = refs.CustomerId, stationId = refs.StationId, operationTypeId = refs.OperationTypeId,
            scheduledArrivalUtc = arrival, scheduledDepartureUtc = arrival.AddHours(2),
            aircraftTypeId = refs.AircraftTypeId, plannedServiceIds = services, assignedStaffMemberIds = roster
        };
        (await FlightMutationAsync(admin, HttpMethod.Put, id, null, PlanningBody(), original.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectFlight();
        var changed = await GetFlightAsync(admin, id);
        changed.ScheduledArrivalUtc.ShouldBe(arrival);
        (await FlightMutationAsync(admin, HttpMethod.Put, id, null, PlanningBody(), changed.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectNone();

        changed = await GetFlightAsync(admin, id);
        (await FlightMutationAsync(admin, HttpMethod.Post, id, "change-number", new { flightNumber = "3911" }, changed.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectFlight();
        changed = await GetFlightAsync(admin, id);
        changed.FlightNumber.ShouldBe("3911");
        (await FlightMutationAsync(admin, HttpMethod.Post, id, "change-number", new { flightNumber = "3911" }, changed.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectNone();
        (await FlightMutationAsync(admin, HttpMethod.Post, id, "change-number", new { flightNumber = "3912" }, original.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await ExpectNone();

        changed = await GetFlightAsync(admin, id);
        (await FlightMutationAsync(admin, HttpMethod.Post, id, "assign", new { staffMemberIds = new[] { secondStaffId } }, changed.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectFlight();
        (await FlightIdsAsync(first)).ShouldNotContain(id);
        (await first.GetAsync($"{Base}/flights/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FlightIdsAsync(second)).ShouldContain(id);
        (await FlightIdsAsync(other)).ShouldNotContain(id);
        changed = await GetFlightAsync(admin, id);
        (await FlightMutationAsync(admin, HttpMethod.Post, id, "assign", new { staffMemberIds = new[] { secondStaffId } }, changed.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectNone();

        changed = await GetFlightAsync(second, id);
        using (var invitation = InvitationRequest(id, [firstStaffId], changed.RowVersion))
            (await second.SendAsync(invitation)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectFlight();
        (await FlightIdsAsync(first)).ShouldContain(id);
        (await GetFlightAsync(second, id)).AssignedEmployees.Select(employee => employee.StaffMemberId)
            .ShouldBe([firstStaffId, secondStaffId], ignoreOrder: true);
        // Repeating an already accepted invite remains a no-op even with the earlier version.
        using (var invitation = InvitationRequest(id, [firstStaffId], changed.RowVersion))
            (await second.SendAsync(invitation)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectNone();

        changed = await GetFlightAsync(admin, id);
        (await FlightMutationAsync(admin, HttpMethod.Post, id, "assign", new { staffMemberIds = new[] { otherStaffId } }, changed.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ExpectNone();
        (await FlightMutationAsync(admin, HttpMethod.Post, id, "assign", new { staffMemberIds = Array.Empty<Guid>() }, original.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await ExpectNone();

        services = [WellKnownMasterDataIds.AircraftPerLandingService];
        roster = [];
        changed = await GetFlightAsync(admin, id);
        (await FlightMutationAsync(admin, HttpMethod.Put, id, null, PlanningBody(), changed.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectFlight();
        (await GetFlightAsync(first, id)).IsPerLanding.ShouldBeTrue();
        (await FlightIdsAsync(second)).ShouldContain(id);
        (await FlightIdsAsync(other)).ShouldNotContain(id);
        services = [refs.ServiceId];
        changed = await GetFlightAsync(admin, id);
        (await FlightMutationAsync(admin, HttpMethod.Put, id, null, PlanningBody(), changed.RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectFlight();
        (await FlightIdsAsync(first)).ShouldNotContain(id);
        (await FlightIdsAsync(second)).ShouldNotContain(id);
        (await FlightIdsAsync(admin)).ShouldContain(id);
        await ExpectNone();
    }

    [Fact]
    public async Task Flight_sync_visibility_rechecks_after_staff_moves_and_station_access_changes()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var firstRefs = await SetupMasterDataAsync(admin);
        var secondRefs = await SetupMasterDataAsync(admin);
        var (first, firstStaffId) = await CreateStaffLoginAsync(admin, firstRefs);
        var (second, _) = await CreateStaffLoginAsync(admin, secondRefs);
        var assigned = await ScheduleFlightAsync(admin, firstRefs, "3920", assignedStaffIds: [firstStaffId]);
        var firstPerLanding = await ScheduleFlightAsync(admin, firstRefs, "3921", [WellKnownMasterDataIds.AircraftPerLandingService]);
        var secondPerLanding = await ScheduleFlightAsync(admin, secondRefs, "3922", [WellKnownMasterDataIds.AircraftPerLandingService]);
        var unassigned = await ScheduleFlightAsync(admin, secondRefs, "3923");
        (await FlightIdsAsync(first)).ShouldBe([assigned, firstPerLanding], ignoreOrder: true);
        await using var firstLive = await CatalogLiveSubscription.ConnectAsync(factory, first);
        await using var secondLive = await CatalogLiveSubscription.ConnectAsync(factory, second);
        async Task ExpectScopeChange() => await Task.WhenAll(
            firstLive.ExpectRefreshesAsync("staff-members", "flights"), secondLive.ExpectRefreshesAsync("staff-members", "flights"));

        (await ReassignStaffAsync(admin, firstStaffId, secondRefs.StationId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectScopeChange();
        var visible = await FlightIdsAsync(first);
        visible.ShouldContain(secondPerLanding);
        visible.ShouldNotContain(assigned);
        visible.ShouldNotContain(firstPerLanding);
        visible.ShouldNotContain(unassigned);
        (await first.GetAsync($"{Base}/flights/{assigned}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FlightIdsAsync(second)).ShouldContain(secondPerLanding);

        await SetCatalogActiveAsync(admin, "stations", secondRefs.StationId, active: false);
        await ExpectScopeChange();
        (await first.GetAsync($"{Base}/flights")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await second.GetAsync($"{Base}/flights")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // Global readers retain the existing API scope; the hint itself contains no flight IDs.
        (await FlightIdsAsync(admin)).ShouldContain(assigned);
        await Task.WhenAll(firstLive.ExpectNoRefreshAsync(), secondLive.ExpectNoRefreshAsync());
    }

    [Fact]
    public async Task Shared_catalog_options_keep_flight_and_lookup_permissions_separate()
    {
        using var anonymous = factory.CreateClient();
        foreach (var catalog in SharedCatalogs)
            (await anonymous.GetAsync($"{MasterDataBase}/{catalog}/options")).StatusCode
                .ShouldBe(HttpStatusCode.Unauthorized, catalog);

        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (flightOnly, _) = await CreateStaffLoginAsync(admin, refs);
        var lookupRoleId = await CreateCatalogRoleAsync(admin, "StationStaff",
            "operations.flights.view", "masterdata.reference.view-options");
        var (lookupClient, _) = await CreateStaffLoginAsync(admin, refs, lookupRoleId);

        foreach (var catalog in SharedCatalogs)
        {
            (await flightOnly.GetAsync($"{MasterDataBase}/{catalog}/options")).StatusCode
                .ShouldBe(HttpStatusCode.Forbidden, catalog);
            (await CatalogOptionsAsync(lookupClient, catalog)).ValueKind.ShouldBe(JsonValueKind.Array, catalog);
        }

        // Lookup access is not catalog-grid access and does not acquire mutation authority.
        (await lookupClient.GetAsync($"{MasterDataBase}/services")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await lookupClient.PostAsJsonAsync($"{MasterDataBase}/services", new { name = "Denied" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var serviceRoleId = await CreateCatalogRoleAsync(admin, "StationStaff",
            "operations.flights.view", "masterdata.services.view");
        var (serviceClient, _) = await CreateStaffLoginAsync(admin, refs, serviceRoleId);
        (await serviceClient.GetAsync($"{MasterDataBase}/services/options")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await serviceClient.GetAsync($"{MasterDataBase}/customers/options")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Administrator_catalog_options_expose_active_shared_lookup_shapes_without_work_order_allowances()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var toolId = await PostForIdAsync(admin, $"{MasterDataBase}/tools",
            new { name = $"Catalog Tool {suffix}", description = (string?)null, equipments = Array.Empty<object>(), calculationType = "Duration" });
        var materialId = await PostForIdAsync(admin, $"{MasterDataBase}/materials",
            new { name = $"Catalog Material {suffix}", description = (string?)null, calculationType = "Quantity" });
        var supportId = await PostForIdAsync(admin, $"{MasterDataBase}/general-supports",
            new { name = $"Catalog Support {suffix}", description = (string?)null, calculationType = "Quantity" });
        var categoryId = await PostForIdAsync(admin, $"{MasterDataBase}/ata-chapter-categories",
            new { name = $"Catalog ATA {suffix}" });
        var chapterId = await PostForIdAsync(admin, $"{MasterDataBase}/ata-chapters",
            new { categoryId, code = $"90-{suffix}", title = $"Catalog Chapter {suffix}" });

        var customer = FindOption(await CatalogOptionsAsync(admin, "customers"), refs.CustomerId);
        customer.GetProperty("iataCode").ValueKind.ShouldBe(JsonValueKind.Null);
        customer.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        var aircraft = FindOption(await CatalogOptionsAsync(admin, "aircraft-types"), refs.AircraftTypeId);
        aircraft.GetProperty("manufacturer").GetString().ShouldBe("Airbus");
        aircraft.GetProperty("model").GetString().ShouldNotBeNullOrWhiteSpace();
        var services = await CatalogOptionsAsync(admin, "services");
        FindOption(services, refs.ServiceId).GetProperty("isAircraftPerLanding").GetBoolean().ShouldBeFalse();
        FindOption(services, WellKnownMasterDataIds.AircraftPerLandingService)
            .GetProperty("isAircraftPerLanding").GetBoolean().ShouldBeTrue();
        services.EnumerateArray().All(service => !service.TryGetProperty("isAllowedPerformedService", out _)).ShouldBeTrue();
        FindOption(await CatalogOptionsAsync(admin, "tools"), toolId)
            .GetProperty("calculationType").GetString().ShouldBe("Duration");
        FindOption(await CatalogOptionsAsync(admin, "materials"), materialId)
            .GetProperty("calculationType").GetString().ShouldBe("Quantity");
        FindOption(await CatalogOptionsAsync(admin, "general-supports"), supportId)
            .GetProperty("calculationType").GetString().ShouldBe("Quantity");
        var chapter = FindOption(await CatalogOptionsAsync(admin, "ata-chapters"), chapterId);
        chapter.GetProperty("categoryId").GetGuid().ShouldBe(categoryId);
        chapter.GetProperty("categoryName").GetString().ShouldBe($"Catalog ATA {suffix}");
        chapter.GetProperty("code").GetString().ShouldBe($"90-{suffix}");
        chapter.GetProperty("title").GetString().ShouldBe($"Catalog Chapter {suffix}");

        await DeactivateReferenceAsync(admin, "customers", refs.CustomerId);
        await DeactivateReferenceAsync(admin, "aircraft-types", refs.AircraftTypeId);
        await DeactivateReferenceAsync(admin, "ata-chapter-categories", categoryId);
        (await CatalogOptionsAsync(admin, "customers")).EnumerateArray()
            .ShouldNotContain(item => item.GetProperty("id").GetGuid() == refs.CustomerId);
        (await CatalogOptionsAsync(admin, "aircraft-types")).EnumerateArray()
            .ShouldNotContain(item => item.GetProperty("id").GetGuid() == refs.AircraftTypeId);
        (await CatalogOptionsAsync(admin, "ata-chapters")).EnumerateArray()
            .ShouldNotContain(item => item.GetProperty("id").GetGuid() == chapterId);
    }

    [Fact]
    public async Task Viewer_can_sync_granted_shared_catalogs_without_catalog_mutation_permissions()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateCatalogRoleAsync(admin, "ViewerOnly",
            "operations.flights.view", "masterdata.reference.view-options");
        var email = $"catalog-viewer-{Guid.NewGuid():N}@example.com";
        var invited = await admin.PostAsJsonAsync($"{IdentityBase}/users/invite", new
        {
            email, displayName = "Catalog viewer", roleId
        });
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync());
        var viewer = await ActivateCatalogClientAsync(admin, email);

        foreach (var catalog in SharedCatalogs)
            (await CatalogOptionsAsync(viewer, catalog)).ValueKind.ShouldBe(JsonValueKind.Array, catalog);
        (await viewer.PostAsJsonAsync($"{MasterDataBase}/aircraft-types", new { manufacturer = "Airbus", model = "Denied" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_contact_options_remain_customer_scoped_and_do_not_unlock_shared_catalogs_or_flights()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var roleId = await CreateCatalogRoleAsync(admin, "CustomerContact", "masterdata.customers.view");
        var email = $"catalog-contact-{Guid.NewGuid():N}@example.com";
        var customer = await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/customers/{refs.CustomerId}");
        using var addContact = new HttpRequestMessage(HttpMethod.Post, $"{MasterDataBase}/customers/{refs.CustomerId}/contacts")
        {
            Content = JsonContent.Create(new { name = "Catalog contact", jobTitle = (string?)null, email, phone = (string?)null, portalAccessRoleId = roleId })
        };
        addContact.Headers.TryAddWithoutValidation("If-Match", customer!.RowVersion);
        var added = await admin.SendAsync(addContact);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        var contact = await ActivateCatalogClientAsync(admin, email);

        var options = await CatalogOptionsAsync(contact, "customers");
        options.EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(refs.CustomerId);
        foreach (var catalog in SharedCatalogs.Where(catalog => catalog != "customers"))
            (await contact.GetAsync($"{MasterDataBase}/{catalog}/options")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, catalog);
        (await contact.GetAsync($"{Base}/flights")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("tools")]
    [InlineData("materials")]
    [InlineData("general-supports")]
    [InlineData("aircraft-types")]
    [InlineData("customers")]
    [InlineData("services")]
    public async Task Catalog_mutations_publish_live_refresh_after_success_without_false_failure_or_noop_events(string catalog)
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Live {suffix}";
        var customerIcao = NextThreeLetterCode();
        var resource = catalog is "tools" or "materials" or "general-supports";
        var calculationType = catalog == "tools" ? "Duration" : "Quantity";

        object Body(string displayName, string? description = null) => catalog switch
        {
            "aircraft-types" => new { manufacturer = "Airbus", model = displayName, notes = description },
            "customers" => new
            {
                iataCode = (string?)null, icaoCode = customerIcao, name = displayName,
                countryId = refs.CountryId, officialEmail = (string?)null, officialPhone = (string?)null,
                address = new { line1 = "1 Airport Rd", line2 = (string?)null, city = "City", region = (string?)null, postalCode = (string?)null },
                contacts = Array.Empty<object>()
            },
            "tools" => new { name = displayName, description, calculationType, equipments = Array.Empty<object>() },
            "materials" or "general-supports" => new { name = displayName, description, calculationType },
            _ => new { name = displayName, description }
        };

        await using var live = await CatalogLiveSubscription.ConnectAsync(factory, admin);
        var id = await PostForIdAsync(admin, $"{MasterDataBase}/{catalog}", Body(name));
        await live.ExpectRefreshAsync(catalog);
        var createdOption = FindOption(await CatalogOptionsAsync(admin, catalog), id);
        createdOption.GetProperty(catalog == "aircraft-types" ? "model" : "name").GetString()
            .ShouldBe(catalog == "aircraft-types" ? name.ToUpperInvariant() : name);
        var createdVersion = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/{catalog}/{id}"))!.RowVersion;

        // Duplicate creates must not publish an invalidation for a rejected transaction.
        (await admin.PostAsJsonAsync($"{MasterDataBase}/{catalog}", Body(name))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await live.ExpectNoRefreshAsync();

        name += " edited";
        if (resource) calculationType = calculationType == "Duration" ? "Quantity" : "Duration";
        (await UpdateCatalogAsync(admin, catalog, id, Body(name), createdVersion)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await live.ExpectRefreshAsync(catalog);
        var editedOption = FindOption(await CatalogOptionsAsync(admin, catalog), id);
        editedOption.GetProperty(catalog == "aircraft-types" ? "model" : "name").GetString()
            .ShouldBe(catalog == "aircraft-types" ? name.ToUpperInvariant() : name);
        if (resource) editedOption.GetProperty("calculationType").GetString().ShouldBe(calculationType);

        if (catalog == "tools")
        {
            var version = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/{catalog}/{id}"))!.RowVersion;
            (await UpdateCatalogAsync(admin, catalog, id, Body(name, "Metadata only"), version))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var tool = await admin.GetFromJsonAsync<JsonElement>($"{MasterDataBase}/{catalog}/{id}");
            tool.GetProperty("description").GetString().ShouldBe("Metadata only");
            await live.ExpectNoRefreshAsync();
        }

        // An old version that would overwrite visible fields must fail without a live hint.
        (await UpdateCatalogAsync(admin, catalog, id, Body(name + " stale"), createdVersion))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await live.ExpectNoRefreshAsync();

        await SetCatalogActiveAsync(admin, catalog, id, active: false);
        await live.ExpectRefreshAsync(catalog);
        (await CatalogOptionsAsync(admin, catalog)).EnumerateArray()
            .ShouldNotContain(option => option.GetProperty("id").GetGuid() == id);
        await SetCatalogActiveAsync(admin, catalog, id, active: false);
        await live.ExpectNoRefreshAsync();

        var inactiveVersion = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/{catalog}/{id}"))!.RowVersion;
        name += " inactive";
        (await UpdateCatalogAsync(admin, catalog, id, Body(name), inactiveVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await live.ExpectNoRefreshAsync();

        await SetCatalogActiveAsync(admin, catalog, id, active: true);
        await live.ExpectRefreshAsync(catalog);
        FindOption(await CatalogOptionsAsync(admin, catalog), id)
            .GetProperty(catalog == "aircraft-types" ? "model" : "name").GetString()
            .ShouldBe(catalog == "aircraft-types" ? name.ToUpperInvariant() : name);
        await SetCatalogActiveAsync(admin, catalog, id, active: true);
        await live.ExpectNoRefreshAsync();
    }

    [Fact]
    public async Task Station_staff_options_preserve_station_scope_active_filter_and_read_permissions()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var first = await SetupMasterDataAsync(admin);
        var second = await SetupMasterDataAsync(admin);
        var lookupRole = await CreateCatalogRoleAsync(admin, "StationStaff", "masterdata.reference.view-options");
        var (lookup, ownStaffId) = await CreateStaffLoginAsync(admin, first, lookupRole);
        var viewRole = await CreateCatalogRoleAsync(admin, "StationStaff", "masterdata.staff-members.view");
        var (staffViewer, _) = await CreateStaffLoginAsync(admin, first, viewRole);
        await SetCatalogActiveAsync(admin, "staff-members", first.StaffMemberId, active: false);

        foreach (var client in new[] { lookup, staffViewer })
        {
            var own = await CatalogOptionsAsync(client, "staff-members");
            own.EnumerateArray().ShouldNotBeEmpty();
            own.EnumerateArray().All(option => option.GetProperty("stationId").GetGuid() == first.StationId).ShouldBeTrue();
            own.EnumerateArray().ShouldNotContain(option => option.GetProperty("id").GetGuid() == first.StaffMemberId);
            own.EnumerateArray().ShouldNotContain(option => option.GetProperty("id").GetGuid() == second.StaffMemberId);
            var crossStation = await client.GetFromJsonAsync<JsonElement>(
                $"{MasterDataBase}/staff-members/options?stationId={second.StationId}");
            crossStation.EnumerateArray().ShouldBeEmpty();
        }

        var global = await CatalogOptionsAsync(admin, "staff-members");
        FindOption(global, ownStaffId).GetProperty("stationId").GetGuid().ShouldBe(first.StationId);
        FindOption(global, second.StaffMemberId).GetProperty("stationId").GetGuid().ShouldBe(second.StationId);
        var staffOption = FindOption(global, ownStaffId);
        staffOption.GetProperty("fullName").GetString().ShouldNotBeNullOrWhiteSpace();
        staffOption.GetProperty("employeeId").GetString().ShouldNotBeNullOrWhiteSpace();
        staffOption.GetProperty("stationCode").GetString().ShouldNotBeNullOrWhiteSpace();
        staffOption.GetProperty("manpowerTypeName").GetString().ShouldNotBeNullOrWhiteSpace();
        staffOption.TryGetProperty("email", out _).ShouldBeFalse();
        staffOption.TryGetProperty("licenses", out _).ShouldBeFalse();
        staffOption.TryGetProperty("rowVersion", out _).ShouldBeFalse();

        var viewerRole = await CreateCatalogRoleAsync(admin, "ViewerOnly", "operations.flights.view", "masterdata.reference.view-options");
        var email = $"staff-catalog-viewer-{Guid.NewGuid():N}@example.com";
        (await admin.PostAsJsonAsync($"{IdentityBase}/users/invite", new
        {
            email, displayName = "Staff catalog viewer", roleId = viewerRole
        })).StatusCode.ShouldBe(HttpStatusCode.Created);
        var viewer = await ActivateCatalogClientAsync(admin, email);
        var viewerOptions = await CatalogOptionsAsync(viewer, "staff-members");
        FindOption(viewerOptions, ownStaffId).GetProperty("stationId").GetGuid().ShouldBe(first.StationId);
        FindOption(viewerOptions, second.StaffMemberId).GetProperty("stationId").GetGuid().ShouldBe(second.StationId);
        (await viewer.PostAsJsonAsync($"{MasterDataBase}/staff-members", new { fullName = "Denied" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Station_staff_mutations_refresh_staff_and_recheck_flight_visibility_after_commit()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var first = await SetupMasterDataAsync(admin);
        var second = await SetupMasterDataAsync(admin);
        var lookupRole = await CreateCatalogRoleAsync(admin, "StationStaff", "masterdata.reference.view-options");
        var (firstClient, _) = await CreateStaffLoginAsync(admin, first, lookupRole);
        var (secondClient, _) = await CreateStaffLoginAsync(admin, second, lookupRole);
        await using var firstLive = await CatalogLiveSubscription.ConnectAsync(factory, firstClient);
        await using var secondLive = await CatalogLiveSubscription.ConnectAsync(factory, secondClient);
        async Task ExpectRefresh(bool scopeChanged = false) => await Task.WhenAll(
            firstLive.ExpectRefreshesAsync(scopeChanged ? ["staff-members", "flights"] : ["staff-members"]),
            secondLive.ExpectRefreshesAsync(scopeChanged ? ["staff-members", "flights"] : ["staff-members"]));
        async Task ExpectNone() => await Task.WhenAll(firstLive.ExpectNoRefreshAsync(), secondLive.ExpectNoRefreshAsync());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Live staff {suffix}";
        var employeeId = $"LIVE-{suffix}";
        var email = $"live-staff-{suffix}@example.com";
        var stationId = first.StationId;
        object Body() => StaffCatalogBody(name, employeeId, email, stationId, first.ManpowerTypeId);

        var id = await PostForIdAsync(admin, $"{MasterDataBase}/staff-members", Body());
        await ExpectRefresh();
        FindOption(await CatalogOptionsAsync(firstClient, "staff-members"), id).GetProperty("fullName").GetString().ShouldBe(name);
        (await CatalogOptionsAsync(secondClient, "staff-members")).EnumerateArray()
            .ShouldNotContain(option => option.GetProperty("id").GetGuid() == id);
        var originalVersion = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/staff-members/{id}"))!.RowVersion;
        (await admin.PostAsJsonAsync($"{MasterDataBase}/staff-members", Body())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await ExpectNone();

        name += " edited";
        employeeId += "X";
        (await UpdateCatalogAsync(admin, "staff-members", id, Body(), originalVersion)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectRefresh();
        var edited = FindOption(await CatalogOptionsAsync(firstClient, "staff-members"), id);
        edited.GetProperty("fullName").GetString().ShouldBe(name);
        edited.GetProperty("employeeId").GetString().ShouldBe(employeeId.ToUpperInvariant());
        var version = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/staff-members/{id}"))!.RowVersion;
        email = $"metadata-staff-{suffix}@example.com";
        (await UpdateCatalogAsync(admin, "staff-members", id, Body(), version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<JsonElement>($"{MasterDataBase}/staff-members/{id}"))
            .GetProperty("email").GetString().ShouldBe(email);
        await ExpectNone();

        var staleBody = StaffCatalogBody(name + " stale", employeeId, email, stationId, first.ManpowerTypeId);
        (await UpdateCatalogAsync(admin, "staff-members", id, staleBody, originalVersion)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await ExpectNone();
        (await ReassignStaffAsync(admin, id, second.StationId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectRefresh(scopeChanged: true);
        (await CatalogOptionsAsync(firstClient, "staff-members")).EnumerateArray()
            .ShouldNotContain(option => option.GetProperty("id").GetGuid() == id);
        FindOption(await CatalogOptionsAsync(secondClient, "staff-members"), id)
            .GetProperty("stationId").GetGuid().ShouldBe(second.StationId);
        (await ReassignStaffAsync(admin, id, second.StationId)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ExpectNone();

        await SetCatalogActiveAsync(admin, "staff-members", id, active: false);
        await ExpectRefresh(scopeChanged: true);
        (await CatalogOptionsAsync(secondClient, "staff-members")).EnumerateArray()
            .ShouldNotContain(option => option.GetProperty("id").GetGuid() == id);
        await SetCatalogActiveAsync(admin, "staff-members", id, active: false);
        await ExpectNone();
        stationId = second.StationId;
        name += " inactive";
        version = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/staff-members/{id}"))!.RowVersion;
        (await UpdateCatalogAsync(admin, "staff-members", id, Body(), version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ExpectNone();
        await SetCatalogActiveAsync(admin, "staff-members", id, active: true);
        await ExpectRefresh(scopeChanged: true);
        FindOption(await CatalogOptionsAsync(secondClient, "staff-members"), id).GetProperty("fullName").GetString().ShouldBe(name);
        await SetCatalogActiveAsync(admin, "staff-members", id, active: true);
        await ExpectNone();
    }

    [Fact]
    public async Task Station_staff_option_dependencies_refresh_affected_data_and_embedded_staff()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        await using var live = await CatalogLiveSubscription.ConnectAsync(factory, admin);
        var station = await admin.GetFromJsonAsync<JsonElement>($"{MasterDataBase}/stations/{refs.StationId}");
        var stationCode = NextThreeLetterCode();
        object StationBody(string name) => new
        {
            iataCode = stationCode, icaoCode = (string?)null, name, city = "City", countryId = refs.CountryId
        };
        var stationName = station.GetProperty("name").GetString()!;
        (await UpdateCatalogAsync(admin, "stations", refs.StationId, StationBody(stationName),
            station.GetProperty("rowVersion").GetString()!)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await live.ExpectRefreshAsync("staff-members");
        FindOption(await CatalogOptionsAsync(admin, "staff-members"), refs.StaffMemberId)
            .GetProperty("stationCode").GetString().ShouldBe(stationCode);
        var version = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/stations/{refs.StationId}"))!.RowVersion;
        (await UpdateCatalogAsync(admin, "stations", refs.StationId, StationBody(stationName + " display only"), version))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await live.ExpectNoRefreshAsync();
        await SetCatalogActiveAsync(admin, "stations", refs.StationId, active: false);
        await live.ExpectRefreshesAsync("staff-members", "flights");
        await SetCatalogActiveAsync(admin, "stations", refs.StationId, active: false);
        await live.ExpectNoRefreshAsync();
        await SetCatalogActiveAsync(admin, "stations", refs.StationId, active: true);
        await live.ExpectRefreshesAsync("staff-members", "flights");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var manpowerName = $"Live manpower {suffix}";
        version = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/manpower-types/{refs.ManpowerTypeId}"))!.RowVersion;
        (await UpdateCatalogAsync(admin, "manpower-types", refs.ManpowerTypeId,
            new { name = manpowerName, description = (string?)null }, version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await live.ExpectRefreshAsync("staff-members");
        FindOption(await CatalogOptionsAsync(admin, "staff-members"), refs.StaffMemberId)
            .GetProperty("manpowerTypeName").GetString().ShouldBe(manpowerName);
        version = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/manpower-types/{refs.ManpowerTypeId}"))!.RowVersion;
        (await UpdateCatalogAsync(admin, "manpower-types", refs.ManpowerTypeId,
            new { name = manpowerName, description = "Metadata only" }, version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await live.ExpectNoRefreshAsync();

        var employeeId = $"EMBED-{suffix}";
        var embeddedStationId = await PostForIdAsync(admin, $"{MasterDataBase}/stations", new
        {
            iataCode = NextThreeLetterCode(), icaoCode = (string?)null, name = $"Embedded station {suffix}", city = "City", countryId = refs.CountryId,
            staff = new[] { new
            {
                fullName = $"Embedded staff {suffix}", employeeId, email = $"embedded-staff-{suffix}@example.com",
                manpowerTypeId = refs.ManpowerTypeId, employmentContract = (object?)null,
                workingDays = (string[]?)null, licenses = Array.Empty<object>(), portalAccessRoleId = (Guid?)null
            } }
        });
        await live.ExpectRefreshAsync("staff-members");
        (await CatalogOptionsAsync(admin, "staff-members")).EnumerateArray()
            .Single(option => option.GetProperty("employeeId").GetString() == employeeId.ToUpperInvariant())
            .GetProperty("stationId").GetGuid().ShouldBe(embeddedStationId);
        await PostForIdAsync(admin, $"{MasterDataBase}/stations", new
        {
            iataCode = NextThreeLetterCode(), icaoCode = (string?)null, name = $"Empty station {suffix}", city = "City", countryId = refs.CountryId
        });
        await live.ExpectNoRefreshAsync();
    }

    private static object StaffCatalogBody(string name, string employeeId, string email, Guid stationId, Guid manpowerTypeId) => new
    {
        fullName = name, employeeId, email, stationId, manpowerTypeId,
        employmentContract = (object?)null, workingDays = (string[]?)null, licenses = Array.Empty<object>(), portalAccessRoleId = (Guid?)null
    };

    private static async Task<HttpResponseMessage> ReassignStaffAsync(HttpClient admin, Guid staffId, Guid stationId)
    {
        var version = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/staff-members/{staffId}"))!.RowVersion;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{MasterDataBase}/staff-members/{staffId}/reassign-station")
        {
            Content = JsonContent.Create(new { stationId })
        };
        request.Headers.TryAddWithoutValidation("If-Match", version);
        return await admin.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UpdateCatalogAsync(
        HttpClient admin, string catalog, Guid id, object body, string rowVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{MasterDataBase}/{catalog}/{id}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        return await admin.SendAsync(request);
    }

    private static async Task SetCatalogActiveAsync(HttpClient admin, string catalog, Guid id, bool active)
    {
        var detail = await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/{catalog}/{id}");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{MasterDataBase}/{catalog}/{id}/{(active ? "activate" : "deactivate")}");
        request.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        var response = await admin.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Real SignalR transport through TestServer; shares this class's disposable SQL fixture.</summary>
    private sealed class CatalogLiveSubscription(HubConnection connection, Channel<MobileSyncChange> changes) : IAsyncDisposable
    {
        public static async Task<CatalogLiveSubscription> ConnectAsync(OperationsApiFactory api, HttpClient authenticatedClient)
        {
            var bearer = authenticatedClient.DefaultRequestHeaders.Authorization!.Parameter;
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(api.Server.BaseAddress, MobileSyncHub.Path), options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                    options.AccessTokenProvider = () => Task.FromResult(bearer);
                })
                .Build();
            var changes = Channel.CreateUnbounded<MobileSyncChange>();
            connection.On<MobileSyncChange>(MobileSyncHub.ChangeClientMethod, change =>
            {
                changes.Writer.TryWrite(change);
            });
            try
            {
                await connection.StartAsync();
                connection.State.ShouldBe(HubConnectionState.Connected);
                return new CatalogLiveSubscription(connection, changes);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        public Task ExpectRefreshAsync(string table) => ExpectRefreshesAsync(table);

        public async Task ExpectRefreshesAsync(params string[] tables)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var received = new List<string>();
            foreach (var _ in tables)
            {
                var change = await changes.Reader.ReadAsync(timeout.Token);
                received.Add(change.Table);
                change.Op.ShouldBe(MobileSyncOps.Refresh);
                change.Audience.ShouldBe(MobileSyncAudience.AllStations);
                change.EntityId.ShouldBeNull();
                change.Payload.ShouldBeNull();
            }
            received.ShouldBe(tables, ignoreOrder: true);
        }

        public async Task ExpectNoRefreshAsync()
        {
            connection.State.ShouldBe(HubConnectionState.Connected);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try
            {
                var change = await changes.Reader.ReadAsync(timeout.Token);
                throw new InvalidOperationException($"Unexpected catalog refresh: {change.Table}.");
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                // A rejected/irrelevant/no-op mutation has no change envelope on the live channel.
            }
            connection.State.ShouldBe(HubConnectionState.Connected);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private static readonly string[] SharedCatalogs =
    [
        "customers", "aircraft-types", "services", "tools", "materials", "general-supports", "ata-chapters", "staff-members"
    ];

    private static Task<Guid> CreateCatalogRoleAsync(HttpClient admin, string userType, params string[] permissions) =>
        PostForIdAsync(admin, $"{IdentityBase}/roles", new
        {
            name = $"Catalog Access {Guid.NewGuid():N}", description = (string?)null,
            compatibleUserType = userType, permissions
        });

    private async Task<HttpClient> ActivateCatalogClientAsync(HttpClient admin, string email)
    {
        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();
        var password = $"Catalogs-{Guid.NewGuid():N}!";
        (await admin.PostAsJsonAsync($"{IdentityBase}/auth/activate", new { email, invitationToken, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();
        return await factory.CreateAuthenticatedClientAsync(email, password);
    }

    private static async Task<JsonElement> CatalogOptionsAsync(HttpClient client, string catalog)
    {
        var response = await client.GetAsync($"{MasterDataBase}/{catalog}/options");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement FindOption(JsonElement options, Guid id) =>
        options.EnumerateArray().Single(option => option.GetProperty("id").GetGuid() == id);

    private static async Task DeactivateReferenceAsync(HttpClient admin, string catalog, Guid id)
    {
        var detail = await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/{catalog}/{id}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{MasterDataBase}/{catalog}/{id}/deactivate");
        request.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        var response = await admin.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<HashSet<Guid>> FlightIdsAsync(HttpClient client)
    {
        var ids = new HashSet<Guid>();
        for (var page = 1; ; page++)
        {
            var response = await client.GetAsync($"{Base}/flights?page={page}&pageSize=200");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var result = (await response.Content.ReadFromJsonAsync<PagedList<FlightListItem>>())!;
            foreach (var item in result.Items) ids.Add(item.Id);
            if (result.Page * result.PageSize >= result.TotalCount) return ids;
        }
    }

    private static async Task<HttpResponseMessage> FlightMutationAsync(
        HttpClient client, HttpMethod method, Guid id, string? operation, object body, string rowVersion)
    {
        using var request = new HttpRequestMessage(method,
            $"{Base}/flights/{id}{(operation is null ? string.Empty : "/" + operation)}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage InvitationRequest(Guid flightId, IReadOnlyList<Guid> staffIds, string? rowVersion = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/flights/{flightId}/invite")
        {
            Content = JsonContent.Create(new { staffMemberIds = staffIds })
        };
        if (rowVersion is not null)
            request.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        return request;
    }

    private static Task<Guid> CreateInviteRoleAsync(HttpClient admin) => PostForIdAsync(admin, $"{IdentityBase}/roles", new
    {
        name = $"Ops Invite {Guid.NewGuid():N}", description = (string?)null,
        compatibleUserType = "StationStaff", permissions = new[] { "operations.flights.view", "operations.flights.invite" }
    });

    private sealed record NotificationPage(List<NotificationItem> Items);
    private sealed record NotificationItem(string Kind, Dictionary<string, string> Payload);

    private sealed record MasterDataRefs(
        Guid CountryId, Guid StationId, Guid CustomerId, Guid OperationTypeId,
        Guid ServiceId, Guid AircraftTypeId, Guid ManpowerTypeId, Guid StaffMemberId, Guid StaffRoleId);

    private async Task<MasterDataRefs> SetupMasterDataAsync(HttpClient admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var countries = await admin.GetFromJsonAsync<PagedList<CountryItem>>($"{MasterDataBase}/countries?page=1&pageSize=1");
        countries!.Items.ShouldNotBeEmpty();
        var countryId = countries.Items[0].Id;

        var stationId = await PostForIdAsync(admin, $"{MasterDataBase}/stations",
            new { iataCode = NextThreeLetterCode(), icaoCode = (string?)null, name = $"Station {suffix}", city = "City", countryId });
        var customerId = await PostForIdAsync(admin, $"{MasterDataBase}/customers", new
        {
            iataCode = (string?)null,
            icaoCode = (string?)null,
            name = $"Customer {suffix}",
            countryId,
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = new { line1 = "1 Airport Rd", line2 = (string?)null, city = "City", region = (string?)null, postalCode = (string?)null },
            contacts = Array.Empty<object>()
        });
        var operationTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/operation-types",
            new { name = $"Transit {suffix}", description = (string?)null });
        var serviceId = await PostForIdAsync(admin, $"{MasterDataBase}/services",
            new { name = $"Marshalling {suffix}", description = (string?)null });
        var aircraftTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/aircraft-types",
            new { manufacturer = "Airbus", model = $"A320-{suffix}", notes = (string?)null });
        var manpowerTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/manpower-types",
            new { name = $"Manpower {suffix}", description = (string?)null });
        await AllowServiceAsync(admin, manpowerTypeId, serviceId);
        var staffMemberId = await PostForIdAsync(admin, $"{MasterDataBase}/staff-members", new
        {
            fullName = $"Ops Staff {suffix}",
            employeeId = $"EMP-{suffix}",
            email = $"ops-staff-{suffix}@example.com",
            stationId,
            manpowerTypeId,
            employmentContract = (object?)null,
            workingDays = (string[]?)null,
            licenses = Array.Empty<object>(),
            portalAccessRoleId = (Guid?)null
        });
        var staffRoleId = await PostForIdAsync(admin, $"{IdentityBase}/roles", new
        {
            name = $"Ops Station Staff {Guid.NewGuid():N}",
            description = (string?)null,
            compatibleUserType = "StationStaff",
            permissions = StaffOperationsPermissions
        });

        return new MasterDataRefs(countryId, stationId, customerId, operationTypeId, serviceId, aircraftTypeId,
            manpowerTypeId, staffMemberId, staffRoleId);
    }

    private static async Task AllowServiceAsync(HttpClient admin, Guid manpowerTypeId, Guid serviceId)
    {
        var detail = await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/manpower-types/{manpowerTypeId}");
        var request = new HttpRequestMessage(HttpMethod.Put, $"{MasterDataBase}/manpower-types/{manpowerTypeId}/service-allowances")
        {
            Content = JsonContent.Create(new { serviceIds = new[] { serviceId } })
        };
        request.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        (await admin.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private sealed record ConcurrencyDetail(string RowVersion);

    private async Task<(HttpClient Client, Guid StaffId)> CreateStaffLoginAsync(HttpClient admin, MasterDataRefs refs) =>
        await CreateStaffLoginAsync(admin, refs, refs.StaffRoleId);

    private async Task<(HttpClient Client, Guid StaffId)> CreateStaffLoginAsync(HttpClient admin, MasterDataRefs refs, Guid roleId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"station-staff-{suffix}@example.com";
        var staffId = await PostForIdAsync(admin, $"{MasterDataBase}/staff-members", new
        {
            fullName = $"Station Staff {suffix}",
            employeeId = $"EMP-{suffix}",
            email,
            stationId = refs.StationId,
            manpowerTypeId = refs.ManpowerTypeId,
            employmentContract = (object?)null,
            workingDays = (string[]?)null,
            licenses = Array.Empty<object>(),
            portalAccessRoleId = roleId
        });

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull($"expected an invitation email for {email}");

        const string password = "StaffPass#12345";
        (await admin.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email, invitationToken, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var client = await factory.CreateAuthenticatedClientAsync(email, password);
        return (client, staffId);
    }

    private static async Task<Guid> ScheduleFlightAsync(
        HttpClient client, MasterDataRefs refs, string flightNumber,
        IReadOnlyList<Guid>? plannedServiceIds = null, IReadOnlyList<Guid>? assignedStaffIds = null)
    {
        var response = await client.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId,
            stationId = refs.StationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber,
            scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(2),
            scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(4),
            aircraftTypeId = (Guid?)null,
            plannedServiceIds = plannedServiceIds ?? [refs.ServiceId],
            assignedStaffMemberIds = assignedStaffIds ?? []
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<FlightDetail> GetFlightAsync(HttpClient client, Guid id)
    {
        var flight = await client.GetFromJsonAsync<FlightDetail>($"{Base}/flights/{id}");
        flight.ShouldNotBeNull();
        return flight!;
    }

    private static async Task<Guid> PostForIdAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {path} failed: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static string NextThreeLetterCode()
    {
        var n = Interlocked.Increment(ref _stationCounter);
        return $"Q{(char)('A' + (n / 26) % 26)}{(char)('A' + n % 26)}";
    }

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);

    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);

    private sealed record FlightListItem(Guid Id, string FlightNumber, bool IsPerLanding);

    private sealed record PlannedService(Guid ServiceId, string Name, bool IsAircraftPerLanding);

    private sealed record AssignedEmployee(Guid StaffMemberId, string FullName, string EmployeeId);

    private sealed record FlightDetail(
        Guid Id,
        string FlightNumber,
        bool IsPerLanding,
        DateTimeOffset ScheduledArrivalUtc,
        DateTimeOffset ScheduledDepartureUtc,
        List<PlannedService> PlannedServices,
        List<AssignedEmployee> AssignedEmployees,
        string RowVersion);
}
