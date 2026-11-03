using System.Net;
using System.Net.Http.Json;
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
            flightNumber = "NGS100",
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
            flightNumber = "NGS101",
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
            flightNumber = "NGS102",
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
            flightNumber = "NGS150",
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

            flight.FlightNumber.ShouldBe("NGS150");
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
        var flightId = await ScheduleFlightAsync(admin, refs, "NGS160");
        var flight = await GetFlightAsync(admin, flightId);

        using var first = new HttpRequestMessage(HttpMethod.Post, $"{Base}/flights/{flightId}/change-number")
        {
            Content = JsonContent.Create(new { flightNumber = "NGS161" })
        };
        first.Headers.TryAddWithoutValidation("If-Match", flight.RowVersion);
        (await admin.SendAsync(first)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stale = new HttpRequestMessage(HttpMethod.Post, $"{Base}/flights/{flightId}/change-number")
        {
            Content = JsonContent.Create(new { flightNumber = "NGS162" })
        };
        stale.Headers.TryAddWithoutValidation("If-Match", flight.RowVersion);
        (await admin.SendAsync(stale)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await GetFlightAsync(admin, flightId)).FlightNumber.ShouldBe("NGS161");
    }

    [Fact]
    public async Task Non_per_landing_flight_is_visible_only_to_assigned_staff()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (assignedClient, assignedStaffId) = await CreateStaffLoginAsync(admin, refs);
        var (unassignedClient, _) = await CreateStaffLoginAsync(admin, refs);

        var flightId = await ScheduleFlightAsync(admin, refs, "NGS400", assignedStaffIds: [assignedStaffId]);

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

        var flightId = await ScheduleFlightAsync(admin, refs, "NGS401",
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
        var flightId = await ScheduleFlightAsync(admin, refs, "NGS402",
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
        var flightId = await ScheduleFlightAsync(admin, refs, "NGS403", assignedStaffIds: [refs.StaffMemberId]);
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
        var flightId = await ScheduleFlightAsync(admin, refs, "NGS404", assignedStaffIds: [refs.StaffMemberId]);
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
        var flightId = await ScheduleFlightAsync(admin, refs, "INV100", assignedStaffIds: [inviterId]);
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
        var flightId = await ScheduleFlightAsync(admin, refs, "INV200", assignedStaffIds: [staffId]);
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
        var flightId = await ScheduleFlightAsync(admin, flightRefs, "INV300");
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
        var flightId = await ScheduleFlightAsync(admin, refs, "INV400", [WellKnownMasterDataIds.AircraftPerLandingService]);
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
        var flightId = await ScheduleFlightAsync(admin, refs, "INV500");
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
        var flightId = await ScheduleFlightAsync(admin, refs, "INV600");
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
