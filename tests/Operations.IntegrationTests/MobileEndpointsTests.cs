using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Operations.IntegrationTests;

/// <summary>
/// End-to-end coverage of the dedicated mobile surface: bearer auth with the refresh token in
/// the JSON body, the offline-sync catch-up endpoint, station-scoped mobile reads, and the
/// idempotent (clientMutationId / clientFlightId) write endpoints backing the mobile outbox.
/// </summary>
public sealed class MobileEndpointsTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    private const string IdentityBase = OperationsApiFactory.IdentityBase;
    private const string MasterDataBase = OperationsApiFactory.MasterDataBase;
    private const string MobileBase = "/api/v1/mobile";
    private static readonly Guid RetiredOnCallServiceId = new("40000000-0000-0000-0000-000000000002");

    private static int _stationCounter;

    private static readonly string[] MobileStaffPermissions =
    [
        "masterdata.reference.view-options",
        "operations.flights.view",
        "operations.work-orders.view",
        "operations.work-orders.author",
        "operations.flights.invite"
    ];

    // --- Mobile auth -------------------------------------------------------------

    [Fact]
    public async Task Mobile_login_returns_refresh_token_in_body_and_refresh_rotates_the_session()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var account = await CreateActivatedStaffAccountAsync(admin, refs, MobileStaffPermissions);

        using var client = factory.CreateClient();

        // Login: the refresh token travels in the JSON body (no cookie dependency).
        var loginResponse = await client.PostAsJsonAsync($"{IdentityBase}/auth/mobile/login",
            new { email = account.Email, password = account.Password });
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<MobileTokens>();
        tokens.ShouldNotBeNull();
        tokens!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();

        // Refresh from the body: a new pair is issued and the old refresh token is revoked (rotation).
        var refreshResponse = await client.PostAsJsonAsync($"{IdentityBase}/auth/mobile/refresh",
            new { refreshToken = tokens.RefreshToken });
        refreshResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = await refreshResponse.Content.ReadFromJsonAsync<MobileTokens>();
        rotated!.RefreshToken.ShouldNotBe(tokens.RefreshToken);

        var replay = await client.PostAsJsonAsync($"{IdentityBase}/auth/mobile/refresh",
            new { refreshToken = tokens.RefreshToken });
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Logout revokes the current session; the rotated refresh token stops working.
        client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
        (await client.PostAsJsonAsync($"{IdentityBase}/auth/mobile/logout",
            new { refreshToken = rotated.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"{IdentityBase}/auth/mobile/refresh",
            new { refreshToken = rotated.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Mobile reads ------------------------------------------------------------

    [Fact]
    public async Task Mobile_reads_serve_station_staff_and_deny_accounts_without_a_staff_link()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);

        // The mobile surface is for station staff; an admin has no staff link and is denied.
        (await admin.GetAsync($"{MobileBase}/me")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var me = await staff.Client.GetFromJsonAsync<MobileMe>($"{MobileBase}/me");
        me.ShouldNotBeNull();
        me!.StaffMemberId.ShouldBe(staff.StaffId);
        me.StationId.ShouldBe(refs.StationId);
        me.StationIata.ShouldNotBeNullOrWhiteSpace();

        var catalogs = await staff.Client.GetFromJsonAsync<MobileCatalogs>($"{MobileBase}/catalogs");
        catalogs!.Services.ShouldContain(s => s.Id == refs.ServiceId);
        catalogs.AllowedPerformedServiceIds.ShouldContain(refs.ServiceId);
        catalogs.Services.ShouldNotContain(s => s.Id == RetiredOnCallServiceId);
        catalogs.Customers.ShouldContain(c => c.Id == refs.CustomerId);

        var roster = await staff.Client.GetFromJsonAsync<List<MobileStaffMember>>($"{MobileBase}/employees/at-my-station");
        roster!.ShouldContain(m => m.StaffMemberId == staff.StaffId);

        // An assigned flight lands on the my-flights list with its planned services and RowVersion.
        var flightId = await ScheduleFlightAsync(admin, refs, "MOB100", [staff.StaffId]);
        var myFlights = await staff.Client.GetFromJsonAsync<List<MobileFlight>>($"{MobileBase}/flights/my");
        var flight = myFlights!.ShouldHaveSingleItem();
        flight.Id.ShouldBe(flightId);
        flight.PlannedServices.ShouldContain(p => p.ServiceId == refs.ServiceId);
        flight.RowVersion.ShouldNotBeNullOrWhiteSpace();
        flight.MyWorkOrder.ShouldBeNull();

        var byId = await staff.Client.GetFromJsonAsync<MobileFlight>($"{MobileBase}/flights/{flightId}");
        byId!.Id.ShouldBe(flightId);
    }

    [Fact]
    public async Task Mobile_catalog_and_work_order_writes_enforce_the_staff_manpower_type_allowances()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);

        var existingFlightId = await ScheduleFlightAsync(admin, refs, "MOB109", [staff.StaffId]);
        var existingSubmit = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{existingFlightId}/work-orders",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
            });
        existingSubmit.StatusCode.ShouldBe(HttpStatusCode.Created, await existingSubmit.Content.ReadAsStringAsync());
        var existingWorkOrder = await existingSubmit.Content.ReadFromJsonAsync<MobileWriteResult>();

        var manpowerType = await admin.GetFromJsonAsync<ConcurrencyDetail>(
            $"{MasterDataBase}/manpower-types/{refs.ManpowerTypeId}");
        var clear = new HttpRequestMessage(
            HttpMethod.Put,
            $"{MasterDataBase}/manpower-types/{refs.ManpowerTypeId}/service-allowances")
        {
            Content = JsonContent.Create(new { serviceIds = Array.Empty<Guid>() })
        };
        clear.Headers.TryAddWithoutValidation("If-Match", manpowerType!.RowVersion);
        (await admin.SendAsync(clear)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var catalogs = await staff.Client.GetFromJsonAsync<MobileCatalogs>($"{MobileBase}/catalogs");
        catalogs!.Services.ShouldContain(service => service.Id == refs.ServiceId);
        catalogs.AllowedPerformedServiceIds.ShouldNotContain(refs.ServiceId);

        var generalOptions = await staff.Client.GetFromJsonAsync<List<CatalogService>>(
            $"{MasterDataBase}/services/options");
        var performedOptions = await staff.Client.GetFromJsonAsync<List<CatalogService>>(
            $"{MasterDataBase}/services/performed-options");
        generalOptions!.ShouldContain(service => service.Id == refs.ServiceId);
        performedOptions!.ShouldNotContain(service => service.Id == refs.ServiceId);

        var flightId = await ScheduleFlightAsync(admin, refs, "MOB110", [staff.StaffId]);
        var submit = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
            });

        submit.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await submit.Content.ReadAsStringAsync());
        (await submit.Content.ReadAsStringAsync()).ShouldContain("Operations.WorkOrder.ServiceNotAllowed");

        var returnToRamp = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/work-orders/{existingWorkOrder!.WorkOrderId}/return-to-ramp",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                serviceLines = Array.Empty<object>(),
                tasks = new[]
                {
                    new
                    {
                        id = (Guid?)null,
                        taskType = "Major",
                        description = "Ramp inspection",
                        fromUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                        toUtc = DateTimeOffset.UtcNow.AddMinutes(20),
                        employeeIds = new[] { staff.StaffId },
                        tools = Array.Empty<object>(),
                        materials = Array.Empty<object>(),
                        generalSupports = Array.Empty<object>()
                    }
                }
            });
        returnToRamp.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await returnToRamp.Content.ReadAsStringAsync());
        (await returnToRamp.Content.ReadAsStringAsync()).ShouldContain("Operations.WorkOrder.ServiceNotAllowed");

        var historical = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{existingWorkOrder.WorkOrderId}");
        historical!.ServiceLines.ShouldContain(line => line.ServiceId == refs.ServiceId);
    }

    [Fact]
    public async Task Out_of_window_assignment_is_absent_from_list_but_available_by_id_as_information_only()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(14);
        var flightId = await ScheduleFlightAsync(
            admin,
            refs,
            "MOB150",
            [staff.StaffId],
            scheduledArrivalUtc);

        var myFlights = await staff.Client.GetFromJsonAsync<List<MobileFlight>>(
            $"{MobileBase}/flights/my?windowHours=168");
        myFlights.ShouldNotBeNull();
        myFlights!.ShouldNotContain(flight => flight.Id == flightId);

        var byId = await staff.Client.GetFromJsonAsync<MobileFlight>($"{MobileBase}/flights/{flightId}");
        byId.ShouldNotBeNull();
        byId!.Id.ShouldBe(flightId);
        byId.IsWithinMobileWindow.ShouldBeFalse();
        byId.MobileWindowStartsAtUtc.ShouldBe(scheduledArrivalUtc.AddHours(-12), TimeSpan.FromSeconds(1));
        byId.MobileWindowEndsAtUtc.ShouldBe(scheduledArrivalUtc.AddHours(12), TimeSpan.FromSeconds(1));

        var action = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
            });
        action.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await action.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sync_changes_returns_refresh_envelopes_for_the_requested_tables()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);

        var all = await staff.Client.GetFromJsonAsync<List<SyncChange>>($"{MobileBase}/sync/changes");
        all.ShouldNotBeNull();
        all.Select(c => c.Table).ShouldContain("flights");
        all.Select(c => c.Table).ShouldContain("flights-per-landing");
        all.Select(c => c.Table).ShouldContain("aircraft-types");
        all.ShouldAllBe(c => c.Op == "refresh");

        var subset = await staff.Client.GetFromJsonAsync<List<SyncChange>>(
            $"{MobileBase}/sync/changes?since={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&tables=flights,customers");
        subset!.Count.ShouldBe(2);
        subset.Select(c => c.Table).ShouldBe(["flights", "customers"], ignoreOrder: true);
    }

    // --- Mobile writes (outbox endpoints) -----------------------------------------

    [Fact]
    public async Task Mobile_write_rejects_non_uuid_mutation_ids()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var flightId = await ScheduleFlightAsync(admin, refs, "MOB190", [staff.StaffId]);

        var response = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId = "../datastore",
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mobile_work_order_submit_is_idempotent_by_client_mutation_id()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var flightId = await ScheduleFlightAsync(admin, refs, "MOB200", [staff.StaffId]);

        var clientMutationId = Guid.NewGuid().ToString();
        var request = new
        {
            clientMutationId,
            workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
        };

        var first = await staff.Client.PostAsJsonAsync($"{MobileBase}/flights/{flightId}/work-orders", request);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var created = await first.Content.ReadFromJsonAsync<MobileWriteResult>();
        created!.Idempotent.ShouldBeFalse();
        created.FlightId.ShouldBe(flightId);

        // Replaying the same mutation (client retry after a lost response) must not duplicate.
        var second = await staff.Client.PostAsJsonAsync($"{MobileBase}/flights/{flightId}/work-orders", request);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replay = await second.Content.ReadFromJsonAsync<MobileWriteResult>();
        replay!.Idempotent.ShouldBeTrue();
        replay.WorkOrderId.ShouldBe(created.WorkOrderId);

        // An idempotency key is bound to the original semantic request. Reusing it for changed
        // content must be rejected instead of pretending the changed work was accepted.
        var mismatched = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId,
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId, remarks: "Changed payload")
            });
        mismatched.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The caller's active work order is embedded on the my-flights row for offline hydration.
        var myFlights = await staff.Client.GetFromJsonAsync<List<MobileFlight>>($"{MobileBase}/flights/my");
        var flight = myFlights!.Single(f => f.Id == flightId);
        flight.Status.ShouldBe("InProgress");
        flight.MyWorkOrder.ShouldNotBeNull();
        flight.MyWorkOrder!.Id.ShouldBe(created.WorkOrderId);
        flight.MyWorkOrder.ServiceLines.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Mobile_work_order_update_rejects_a_stale_offline_base_revision()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var flightId = await ScheduleFlightAsync(admin, refs, "MOB250", [staff.StaffId]);

        var submit = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
            });
        submit.StatusCode.ShouldBe(HttpStatusCode.Created, await submit.Content.ReadAsStringAsync());
        var created = await submit.Content.ReadFromJsonAsync<MobileWriteResult>();

        var original = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{created!.WorkOrderId}");
        original.ShouldNotBeNull();
        original!.RowVersion.ShouldNotBeNullOrWhiteSpace();

        var accepted = await staff.Client.PutAsJsonAsync(
            $"{MobileBase}/work-orders/{created.WorkOrderId}",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                baseRowVersion = original.RowVersion,
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId, remarks: "Newer portal-equivalent edit")
            });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        // This request was prepared from the original offline snapshot. It must conflict rather
        // than overwrite the edit accepted immediately above.
        var stale = await staff.Client.PutAsJsonAsync(
            $"{MobileBase}/work-orders/{created.WorkOrderId}",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                baseRowVersion = original.RowVersion,
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId, remarks: "Stale offline edit")
            });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync());

        var current = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{created.WorkOrderId}");
        current!.Remarks.ShouldBe("Newer portal-equivalent edit");
    }

    [Fact]
    public async Task Mobile_scratch_create_dedupes_by_client_flight_id()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);

        var clientMutationId = Guid.NewGuid().ToString();
        var clientFlightId = Guid.NewGuid();
        var request = new
        {
            clientMutationId,
            clientFlightId,
            customerId = refs.CustomerId,
            flightNumber = "MOB300",
            scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(-2),
            scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(2),
            aircraftTypeId = refs.AircraftTypeId,
            plannedServiceIds = new[] { refs.ServiceId },
            workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
        };

        var first = await staff.Client.PostAsJsonAsync($"{MobileBase}/work-orders/scratch", request);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var created = await first.Content.ReadFromJsonAsync<MobileWriteResult>();

        // Same mutation id: idempotent replay.
        var replayResponse = await staff.Client.PostAsJsonAsync($"{MobileBase}/work-orders/scratch", request);
        replayResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replay = await replayResponse.Content.ReadFromJsonAsync<MobileWriteResult>();
        replay!.Idempotent.ShouldBeTrue();
        replay.WorkOrderId.ShouldBe(created!.WorkOrderId);

        // Different mutation, same offline flight identity: duplicate scratch flight is a conflict.
        var duplicate = await staff.Client.PostAsJsonAsync($"{MobileBase}/work-orders/scratch",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                clientFlightId,
                customerId = refs.CustomerId,
                flightNumber = "MOB300",
                scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(-2),
                scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(2),
                aircraftTypeId = refs.AircraftTypeId,
                plannedServiceIds = new[] { refs.ServiceId },
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
            });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Mobile_cancel_and_return_to_ramp_flow_through_the_work_order_rules()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var canceller = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);

        // Cancel files a cancellation work order and moves the flight to InProgress.
        var cancelFlightId = await ScheduleFlightAsync(admin, refs, "MOB400", [canceller.StaffId]);
        var cancel = await canceller.Client.PostAsJsonAsync($"{MobileBase}/flights/{cancelFlightId}/cancel",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                canceledAtUtc = DateTimeOffset.UtcNow,
                reason = "Weather diversion"
            });
        cancel.StatusCode.ShouldBe(HttpStatusCode.Created, await cancel.Content.ReadAsStringAsync());
        var cancelResult = await cancel.Content.ReadFromJsonAsync<MobileWriteResult>();
        var cancelWo = await canceller.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{cancelResult!.WorkOrderId}");
        cancelWo!.Type.ShouldBe("Cancellation");
        cancelWo.CancellationReason.ShouldBe("Weather diversion");

        // Return-to-ramp appends lines onto the author's editable work order.
        var rtrFlightId = await ScheduleFlightAsync(admin, refs, "MOB500", [author.StaffId]);
        var submit = await author.Client.PostAsJsonAsync($"{MobileBase}/flights/{rtrFlightId}/work-orders",
            new { clientMutationId = Guid.NewGuid().ToString(), workOrder = CompletionWorkOrderBody(refs, author.StaffId) });
        submit.StatusCode.ShouldBe(HttpStatusCode.Created, await submit.Content.ReadAsStringAsync());
        var submitted = await submit.Content.ReadFromJsonAsync<MobileWriteResult>();

        var rtr = await author.Client.PostAsJsonAsync($"{MobileBase}/work-orders/{submitted!.WorkOrderId}/return-to-ramp",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                serviceLines = new[]
                {
                    new
                    {
                        serviceId = refs.ServiceId,
                        performedByStaffMemberId = author.StaffId,
                        fromUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                        toUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                        description = "Return to ramp"
                    }
                },
                tasks = Array.Empty<object>()
            });
        rtr.StatusCode.ShouldBe(HttpStatusCode.OK, await rtr.Content.ReadAsStringAsync());

        var detail = await author.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{submitted.WorkOrderId}");
        detail!.ServiceLines.Count.ShouldBe(2);
    }

    // --- Helpers -------------------------------------------------------------------

    private static object CompletionWorkOrderBody(
        MasterDataRefs refs,
        Guid performerId,
        string remarks = "Mobile submission") => new
    {
        type = "Completion",
        actualFlightNumber = "MOB999",
        aircraftTypeId = refs.AircraftTypeId,
        aircraftTailNumber = "HZ-TEST",
        actualArrivalUtc = DateTimeOffset.UtcNow.AddHours(-1),
        actualDepartureUtc = DateTimeOffset.UtcNow.AddHours(1),
        remarks,
        serviceLines = new[]
        {
            new
            {
                serviceId = refs.ServiceId,
                performedByStaffMemberId = performerId,
                fromUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                toUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                description = "Handled"
            }
        },
        tasks = Array.Empty<object>()
    };

    private sealed record MasterDataRefs(
        Guid CountryId, Guid StationId, Guid CustomerId, Guid OperationTypeId, Guid ServiceId,
        Guid ManpowerTypeId, Guid AircraftTypeId);

    private async Task<MasterDataRefs> SetupMasterDataAsync(HttpClient admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var countries = await admin.GetFromJsonAsync<PagedList<CountryItem>>($"{MasterDataBase}/countries?page=1&pageSize=1");
        var countryId = countries!.Items[0].Id;

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
        var manpowerTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/manpower-types",
            new { name = $"Manpower {suffix}", description = (string?)null });
        var aircraftTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/aircraft-types",
            new { manufacturer = "Airbus", model = $"A320-{suffix}", notes = (string?)null });

        var manpowerType = await admin.GetFromJsonAsync<ConcurrencyDetail>(
            $"{MasterDataBase}/manpower-types/{manpowerTypeId}");
        var allowanceRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"{MasterDataBase}/manpower-types/{manpowerTypeId}/service-allowances")
        {
            Content = JsonContent.Create(new { serviceIds = new[] { serviceId } })
        };
        allowanceRequest.Headers.TryAddWithoutValidation("If-Match", manpowerType!.RowVersion);
        (await admin.SendAsync(allowanceRequest)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return new MasterDataRefs(countryId, stationId, customerId, operationTypeId, serviceId, manpowerTypeId, aircraftTypeId);
    }

    private sealed record StaffAccount(string Email, string Password, Guid StaffId);

    /// <summary>Provisions + activates a StationStaff account without logging in (no MFA enrollment).</summary>
    private async Task<StaffAccount> CreateActivatedStaffAccountAsync(HttpClient admin, MasterDataRefs refs, string[] permissions)
    {
        var roleId = await PostForIdAsync(admin, $"{IdentityBase}/roles", new
        {
            name = $"Mobile Role {Guid.NewGuid():N}",
            description = (string?)null,
            compatibleUserType = "StationStaff",
            permissions
        });

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"mobile-staff-{suffix}@example.com";
        var staffId = await PostForIdAsync(admin, $"{MasterDataBase}/staff-members", new
        {
            fullName = $"Mobile Staff {suffix}",
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

        return new StaffAccount(email, password, staffId);
    }

    private async Task<(HttpClient Client, Guid StaffId)> CreateStaffLoginAsync(HttpClient admin, MasterDataRefs refs, string[] permissions)
    {
        var account = await CreateActivatedStaffAccountAsync(admin, refs, permissions);
        var client = await factory.CreateAuthenticatedClientAsync(account.Email, account.Password);
        return (client, account.StaffId);
    }

    private static async Task<Guid> ScheduleFlightAsync(
        HttpClient client,
        MasterDataRefs refs,
        string flightNumber,
        IReadOnlyList<Guid>? assignedStaffIds = null,
        DateTimeOffset? scheduledArrivalUtc = null)
    {
        var arrivalUtc = scheduledArrivalUtc ?? DateTimeOffset.UtcNow.AddHours(2);
        var response = await client.PostAsJsonAsync($"{OperationsApiFactory.Base}/flights", new
        {
            customerId = refs.CustomerId,
            stationId = refs.StationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber,
            scheduledArrivalUtc = arrivalUtc,
            scheduledDepartureUtc = arrivalUtc.AddHours(2),
            aircraftTypeId = (Guid?)null,
            plannedServiceIds = new[] { refs.ServiceId },
            assignedStaffMemberIds = assignedStaffIds ?? []
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
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
        return $"M{(char)('A' + (n / 26) % 26)}{(char)('A' + n % 26)}";
    }

    // --- Response mirrors -------------------------------------------------------

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);

    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);

    private sealed record MobileTokens(
        string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc,
        string RefreshToken, DateTimeOffset RefreshTokenExpiresAtUtc);

    private sealed record MobileMe(
        Guid StaffMemberId, string FullName, string EmployeeId,
        Guid StationId, string StationIata, string StationName,
        Guid ManpowerTypeId, string? ManpowerTypeName);

    private sealed record MobileCatalogs(
        List<CatalogService> Services, List<Guid> AllowedPerformedServiceIds,
        List<CatalogItem> Tools, List<CatalogItem> Materials,
        List<CatalogItem> GeneralSupports, List<CatalogCustomer> Customers, List<CatalogAircraftType> AircraftTypes,
        DateTimeOffset GeneratedAtUtc);

    private sealed record CatalogService(Guid Id, string Name, bool IsAircraftPerLanding);
    private sealed record ConcurrencyDetail(string RowVersion);
    private sealed record CatalogItem(Guid Id, string Name);
    private sealed record CatalogCustomer(Guid Id, string? IataCode, string Name);
    private sealed record CatalogAircraftType(Guid Id, string Manufacturer, string Model);

    private sealed record MobileStaffMember(Guid StaffMemberId, string FullName, string EmployeeId);

    private sealed record MobileFlight(
        Guid Id, string FlightNumber, string Status, bool IsPerLanding, bool IsAdHoc,
        List<PlannedService> PlannedServices, WorkOrderDetail? MyWorkOrder,
        bool OtherWorkOrdersExist, string RowVersion,
        bool IsWithinMobileWindow, DateTimeOffset MobileWindowStartsAtUtc, DateTimeOffset MobileWindowEndsAtUtc);

    private sealed record PlannedService(Guid ServiceId, string Name, bool IsAircraftPerLanding);

    private sealed record WorkOrderDetail(
        Guid Id, Guid FlightId, string Type, string Status,
        string? CancellationReason, string? Remarks, List<ServiceLine> ServiceLines, string RowVersion);

    private sealed record ServiceLine(Guid Id, Guid ServiceId, string ServiceName);

    private sealed record SyncChange(string Table, string Op, string? EntityId, string Audience, DateTimeOffset Version);

    private sealed record MobileWriteResult(Guid WorkOrderId, Guid FlightId, bool Idempotent);
}
