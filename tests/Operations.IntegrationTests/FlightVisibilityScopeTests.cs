using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Operations.IntegrationTests;

/// <summary>
/// Flight visibility tiers: station dispatchers (station staff holding
/// <c>operations.flights.view-station</c>) see every flight at their own station without being on
/// the assigned-employee roster while staying blind to other stations. Regular station staff keep
/// the default Per-Landing-plus-assigned visibility.
/// </summary>
public sealed class FlightVisibilityScopeTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    private const string Base = OperationsApiFactory.Base;
    private const string MasterDataBase = OperationsApiFactory.MasterDataBase;
    private const string IdentityBase = OperationsApiFactory.IdentityBase;

    private static int _stationCounter;

    private static readonly string[] DispatcherPermissions =
    [
        "operations.flights.view",
        "operations.flights.view-station"
    ];

    private static readonly string[] RegularStaffPermissions =
    [
        "operations.flights.view"
    ];

    [Fact]
    public async Task Station_dispatcher_sees_unassigned_flights_at_own_station_but_regular_staff_do_not()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var dispatcher = await CreateStaffLoginAsync(admin, refs, DispatcherPermissions);
        var regular = await CreateStaffLoginAsync(admin, refs, RegularStaffPermissions);

        // A non-Per-Landing flight with nobody assigned.
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "VIS100");

        // Dispatcher: full station-wide visibility without assignment.
        (await dispatcher.Client.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var dispatcherList = await dispatcher.Client.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        dispatcherList!.Items.ShouldContain(f => f.Id == flightId);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var calendar = await dispatcher.Client.GetFromJsonAsync<List<CalendarFlight>>($"{Base}/flights/calendar?fromUtc={from}&toUtc={to}");
        calendar!.ShouldContain(f => f.Id == flightId);

        // Regular staff without view-station keep assigned-only visibility.
        (await regular.Client.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var regularList = await regular.Client.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        regularList!.Items.ShouldNotContain(f => f.Id == flightId);
    }

    [Fact]
    public async Task Station_dispatcher_cannot_see_flights_at_other_stations()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var otherStationId = await CreateStationAsync(admin, refs.CountryId);
        var dispatcher = await CreateStaffLoginAsync(admin, refs, DispatcherPermissions);

        var otherStationFlightId = await ScheduleFlightAsync(admin, otherStationId, refs, "VIS200");

        (await dispatcher.Client.GetAsync($"{Base}/flights/{otherStationFlightId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var list = await dispatcher.Client.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        list!.Items.ShouldNotContain(f => f.Id == otherStationFlightId);
    }

    // --- Helpers ---------------------------------------------------------------

    private sealed record MasterDataRefs(
        Guid CountryId, Guid StationId, Guid CustomerId, Guid OperationTypeId, Guid ServiceId, Guid ManpowerTypeId);

    private async Task<MasterDataRefs> SetupMasterDataAsync(HttpClient admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var countries = await admin.GetFromJsonAsync<PagedList<CountryItem>>($"{MasterDataBase}/countries?page=1&pageSize=1");
        countries!.Items.ShouldNotBeEmpty();
        var countryId = countries.Items[0].Id;

        var stationId = await CreateStationAsync(admin, countryId);
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
        var manpowerTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/manpower-types",
            new { name = $"Manpower {suffix}", description = (string?)null });
        await AllowServiceAsync(admin, manpowerTypeId, serviceId);

        return new MasterDataRefs(countryId, stationId, customerId, operationTypeId, serviceId, manpowerTypeId);
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

    private static async Task<Guid> CreateStationAsync(HttpClient admin, Guid countryId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return await PostForIdAsync(admin, $"{MasterDataBase}/stations",
            new { iataCode = NextThreeLetterCode(), icaoCode = (string?)null, name = $"Station {suffix}", city = "City", countryId });
    }

    /// <summary>Creates a staff member at the refs station with a role holding <paramref name="permissions"/> and returns a logged-in client.</summary>
    private async Task<(HttpClient Client, Guid StaffId)> CreateStaffLoginAsync(HttpClient admin, MasterDataRefs refs, string[] permissions)
    {
        var roleId = await PostForIdAsync(admin, $"{IdentityBase}/roles", new
        {
            name = $"Visibility Role {Guid.NewGuid():N}",
            description = (string?)null,
            compatibleUserType = "StationStaff",
            permissions
        });

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"visibility-staff-{suffix}@example.com";
        var staffId = await PostForIdAsync(admin, $"{MasterDataBase}/staff-members", new
        {
            fullName = $"Visibility Staff {suffix}",
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
        HttpClient client, Guid stationId, MasterDataRefs refs, string flightNumber,
        IReadOnlyList<Guid>? assignedStaffIds = null)
    {
        var response = await client.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId,
            stationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber,
            scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(2),
            scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(4),
            aircraftTypeId = (Guid?)null,
            plannedServiceIds = new[] { refs.ServiceId },
            assignedStaffMemberIds = assignedStaffIds ?? []
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> PostForIdAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {path} failed: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    // Unique IATA generator with a prefix distinct from other test classes in this assembly.
    private static string NextThreeLetterCode()
    {
        var n = Interlocked.Increment(ref _stationCounter);
        return $"V{(char)('A' + (n / 26) % 26)}{(char)('A' + n % 26)}";
    }

    // --- Response mirrors -------------------------------------------------------

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);

    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);

    private sealed record FlightListItem(Guid Id, string FlightNumber, string Status, bool IsPerLanding);

    private sealed record CalendarFlight(Guid Id, string FlightNumber, string Status);

}
