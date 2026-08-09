using System.Net;
using System.Net.Http.Json;
using System.Text;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Infrastructure.Persistence;
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
        var suffix = Guid.NewGuid().ToString("N");
        var toolId = await PostForIdAsync(admin, $"{MasterDataBase}/tools", new
        {
            name = $"Duration Tool {suffix}",
            description = (string?)null,
            equipments = Array.Empty<object>()
        });
        var materialId = await PostForIdAsync(admin, $"{MasterDataBase}/materials", new
        {
            name = $"Duration Material {suffix}",
            description = (string?)null,
            calculationType = "Duration"
        });
        var supportId = await PostForIdAsync(admin, $"{MasterDataBase}/general-supports", new
        {
            name = $"Quantity Support {suffix}",
            description = (string?)null
        });

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
        catalogs.Tools.ShouldContain(item => item.Id == toolId && item.CalculationType == "Duration");
        catalogs.Materials.ShouldContain(item => item.Id == materialId && item.CalculationType == "Duration");
        catalogs.GeneralSupports.ShouldContain(item => item.Id == supportId && item.CalculationType == "Quantity");

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
    public async Task Mobile_writes_reject_new_disallowed_services_without_invalidating_historical_rows()
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

        var occurrenceFrom = DateTimeOffset.UtcNow.AddMinutes(-20);
        var occurrenceTo = DateTimeOffset.UtcNow.AddMinutes(20);
        var taskOnlyReturnToRamp = await staff.Client.PostAsJsonAsync(
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
                        fromUtc = occurrenceFrom,
                        toUtc = occurrenceTo,
                        employeeIds = new[] { staff.StaffId },
                        tools = Array.Empty<object>(),
                        materials = Array.Empty<object>(),
                        generalSupports = Array.Empty<object>()
                    }
                }
            });
        taskOnlyReturnToRamp.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await taskOnlyReturnToRamp.Content.ReadAsStringAsync());

        var legacyNewDisallowedService = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/work-orders/{existingWorkOrder.WorkOrderId}/return-to-ramp",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                serviceLines = new[]
                {
                    new
                    {
                        serviceId = refs.ServiceId,
                        performedByStaffMemberIds = new[] { staff.StaffId },
                        fromUtc = occurrenceFrom,
                        toUtc = occurrenceTo,
                        description = "New disallowed legacy service"
                    }
                },
                tasks = Array.Empty<object>()
            });
        legacyNewDisallowedService.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            await legacyNewDisallowedService.Content.ReadAsStringAsync());
        (await legacyNewDisallowedService.Content.ReadAsStringAsync())
            .ShouldContain("Operations.WorkOrder.ServiceNotAllowed");

        var canonicalNewDisallowedService = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{existingFlightId}/return-to-ramps",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                fromUtc = occurrenceFrom,
                toUtc = occurrenceTo,
                description = "New disallowed canonical occurrence",
                serviceLines = new[]
                {
                    new
                    {
                        serviceId = refs.ServiceId,
                        performedByStaffMemberIds = new[] { staff.StaffId },
                        fromUtc = occurrenceFrom,
                        toUtc = occurrenceTo,
                        description = "New disallowed canonical service"
                    }
                },
                tasks = Array.Empty<object>()
            });
        canonicalNewDisallowedService.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            await canonicalNewDisallowedService.Content.ReadAsStringAsync());
        (await canonicalNewDisallowedService.Content.ReadAsStringAsync())
            .ShouldContain("Operations.WorkOrder.ServiceNotAllowed");

        var historical = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{existingWorkOrder.WorkOrderId}");
        historical!.ServiceLines.ShouldContain(line => line.ServiceId == refs.ServiceId);
        historical.ReturnToRamps!.Count.ShouldBe(1);
        historical.ReturnToRamps.Single().Tasks.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Mobile_work_order_round_trips_multiple_service_performers()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var coworker = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var flightId = await ScheduleFlightAsync(
            admin,
            refs,
            "MOB111",
            [author.StaffId, coworker.StaffId]);

        var submit = await author.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                workOrder = CompletionWorkOrderBody(refs, [author.StaffId, coworker.StaffId])
            });

        submit.StatusCode.ShouldBe(HttpStatusCode.Created, await submit.Content.ReadAsStringAsync());
        var write = await submit.Content.ReadFromJsonAsync<MobileWriteResult>();
        var detail = await author.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{write!.WorkOrderId}");

        var performers = detail!.ServiceLines.ShouldHaveSingleItem().PerformedBy;
        performers.Select(performer => performer.StaffMemberId)
            .ShouldBe([author.StaffId, coworker.StaffId], ignoreOrder: true);
    }

    [Fact]
    public async Task Mobile_work_order_round_trips_snapshotted_resource_usage_for_normal_and_return_to_ramp_tasks()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var suffix = Guid.NewGuid().ToString("N");
        var toolId = await PostForIdAsync(admin, $"{MasterDataBase}/tools", new
        {
            name = $"Open duration tool {suffix}",
            description = (string?)null,
            equipments = Array.Empty<object>()
        });
        var materialId = await PostForIdAsync(admin, $"{MasterDataBase}/materials", new
        {
            name = $"Quantity material {suffix}",
            description = (string?)null
        });
        var supportId = await PostForIdAsync(admin, $"{MasterDataBase}/general-supports", new
        {
            name = $"Quantity support {suffix}",
            description = (string?)null
        });
        var flightId = await ScheduleFlightAsync(admin, refs, "MOB112", [staff.StaffId]);
        var taskFrom = DateTimeOffset.UtcNow.AddMinutes(-20);
        var taskTo = taskFrom.AddMinutes(30);

        var submit = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                workOrder = new
                {
                    type = "Completion",
                    actualFlightNumber = "MOB112",
                    aircraftTypeId = refs.AircraftTypeId,
                    aircraftTailNumber = "HZ-TEST",
                    actualArrivalUtc = taskFrom.AddHours(-1),
                    actualDepartureUtc = taskTo.AddHours(1),
                    remarks = "Resource usage round trip",
                    serviceLines = new[]
                    {
                        new
                        {
                            serviceId = refs.ServiceId,
                            performedByStaffMemberIds = new[] { staff.StaffId },
                            fromUtc = taskFrom,
                            toUtc = taskTo,
                            description = "Handled"
                        }
                    },
                    tasks = new[]
                    {
                        new
                        {
                            id = (Guid?)null,
                            taskType = "Minor",
                            description = "Normal task resources",
                            fromUtc = taskFrom,
                            toUtc = taskTo,
                            employeeIds = new[] { staff.StaffId },
                            tools = new[]
                            {
                                new
                                {
                                    toolId,
                                    quantity = (decimal?)null,
                                    fromUtc = (DateTimeOffset?)taskFrom,
                                    toUtc = (DateTimeOffset?)null
                                }
                            },
                            materials = new[]
                            {
                                new
                                {
                                    materialId,
                                    quantity = (decimal?)2.25m,
                                    fromUtc = (DateTimeOffset?)null,
                                    toUtc = (DateTimeOffset?)null
                                }
                            },
                            generalSupports = new[]
                            {
                                new
                                {
                                    generalSupportId = supportId,
                                    quantity = (decimal?)3m,
                                    fromUtc = (DateTimeOffset?)null,
                                    toUtc = (DateTimeOffset?)null
                                }
                            }
                        }
                    }
                }
            });
        submit.StatusCode.ShouldBe(HttpStatusCode.Created, await submit.Content.ReadAsStringAsync());
        var created = await submit.Content.ReadFromJsonAsync<MobileWriteResult>();

        var detail = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{created!.WorkOrderId}");
        AssertResourceUsage(detail!.Tasks.ShouldHaveSingleItem(), taskFrom, toolId, materialId, supportId);

        var rtrFrom = taskFrom.AddMinutes(2);
        var rtrTo = taskTo.AddMinutes(-2);
        var rtr = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/work-orders/{created.WorkOrderId}/return-to-ramp",
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
                        description = "RTR task resources",
                        fromUtc = rtrFrom,
                        toUtc = rtrTo,
                        employeeIds = new[] { staff.StaffId },
                        tools = new[]
                        {
                            new
                            {
                                toolId,
                                quantity = (decimal?)null,
                                fromUtc = (DateTimeOffset?)rtrFrom,
                                toUtc = (DateTimeOffset?)null
                            }
                        },
                        materials = new[]
                        {
                            new
                            {
                                materialId,
                                quantity = (decimal?)2.25m,
                                fromUtc = (DateTimeOffset?)null,
                                toUtc = (DateTimeOffset?)null
                            }
                        },
                        generalSupports = new[]
                        {
                            new
                            {
                                generalSupportId = supportId,
                                quantity = (decimal?)3m,
                                fromUtc = (DateTimeOffset?)null,
                                toUtc = (DateTimeOffset?)null
                            }
                        }
                    }
                }
            });
        rtr.StatusCode.ShouldBe(HttpStatusCode.OK, await rtr.Content.ReadAsStringAsync());

        detail = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{created.WorkOrderId}");
        var occurrence = detail!.ReturnToRamps!.ShouldHaveSingleItem();
        occurrence.FromUtc.ShouldBe(rtrFrom);
        occurrence.ToUtc.ShouldBe(rtrTo);
        AssertResourceUsage(occurrence.Tasks.ShouldHaveSingleItem(), rtrFrom, toolId, materialId, supportId);
    }

    private static void AssertResourceUsage(
        TaskLine task,
        DateTimeOffset expectedToolFrom,
        Guid toolId,
        Guid materialId,
        Guid supportId)
    {
        var tool = task.Tools.ShouldHaveSingleItem();
        tool.ResourceId.ShouldBe(toolId);
        tool.CalculationType.ShouldBe("Duration");
        tool.Quantity.ShouldBeNull();
        tool.FromUtc.ShouldBe(expectedToolFrom);
        tool.ToUtc.ShouldBeNull();

        var material = task.Materials.ShouldHaveSingleItem();
        material.ResourceId.ShouldBe(materialId);
        material.CalculationType.ShouldBe("Quantity");
        material.Quantity.ShouldBe(2.25m);
        material.FromUtc.ShouldBeNull();
        material.ToUtc.ShouldBeNull();

        var support = task.GeneralSupports.ShouldHaveSingleItem();
        support.ResourceId.ShouldBe(supportId);
        support.CalculationType.ShouldBe("Quantity");
        support.Quantity.ShouldBe(3m);
        support.FromUtc.ShouldBeNull();
        support.ToUtc.ShouldBeNull();
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
    public async Task Canonical_mobile_return_to_ramp_commits_bound_mutation_and_replays_same_ids()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var flightId = await ScheduleFlightAsync(admin, refs, "MOB201", [staff.StaffId]);
        var submittedResponse = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId)
            });
        submittedResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await submittedResponse.Content.ReadAsStringAsync());
        var submitted = await submittedResponse.Content.ReadFromJsonAsync<MobileWriteResult>();

        var occurrenceFrom = DateTimeOffset.UtcNow.AddMinutes(-15);
        var occurrenceTo = DateTimeOffset.UtcNow.AddMinutes(15);
        var clientMutationId = Guid.NewGuid().ToString();
        var request = new
        {
            clientMutationId,
            fromUtc = occurrenceFrom,
            toUtc = occurrenceTo,
            description = "Canonical mobile occurrence",
            serviceLines = new[]
            {
                new
                {
                    serviceId = refs.ServiceId,
                    performedByStaffMemberIds = new[] { staff.StaffId },
                    fromUtc = occurrenceFrom,
                    toUtc = occurrenceTo,
                    description = "Marshaller returned"
                }
            },
            tasks = new[]
            {
                new
                {
                    id = (Guid?)null,
                    taskType = "Minor",
                    description = "Inspect stand",
                    fromUtc = occurrenceFrom,
                    toUtc = occurrenceTo,
                    employeeIds = new[] { staff.StaffId },
                    tools = Array.Empty<object>(),
                    materials = Array.Empty<object>(),
                    generalSupports = Array.Empty<object>(),
                    attachments = Array.Empty<object>()
                }
            }
        };

        var firstResponse = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/return-to-ramps", request);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await firstResponse.Content.ReadAsStringAsync());
        var first = await firstResponse.Content.ReadFromJsonAsync<MobileWriteResult>();
        first!.Idempotent.ShouldBeFalse();
        first.WorkOrderId.ShouldBe(submitted!.WorkOrderId);
        first.FlightId.ShouldBe(flightId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var committed = await db.MobileMutations.AsNoTracking()
                .SingleAsync(item => item.ClientMutationId == clientMutationId);
            committed.WorkOrderId.ShouldBe(first.WorkOrderId);
            committed.WorkOrderId.ShouldNotBe(Guid.Empty);
            committed.FlightId.ShouldBe(flightId);
        }

        var replayResponse = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/return-to-ramps", request);
        replayResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await replayResponse.Content.ReadAsStringAsync());
        var replay = await replayResponse.Content.ReadFromJsonAsync<MobileWriteResult>();
        replay!.Idempotent.ShouldBeTrue();
        replay.WorkOrderId.ShouldBe(first.WorkOrderId);
        replay.FlightId.ShouldBe(first.FlightId);

        var detail = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{first.WorkOrderId}");
        var occurrence = detail!.ReturnToRamps!.ShouldHaveSingleItem();
        occurrence.Description.ShouldBe("Canonical mobile occurrence");
        occurrence.ServiceLines.ShouldHaveSingleItem();
        occurrence.Tasks.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Mobile_service_attachment_survives_stable_id_update_and_supports_download_and_delete()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var flightId = await ScheduleFlightAsync(admin, refs, "MOB225", [staff.StaffId]);
        var now = DateTimeOffset.UtcNow;
        var fileContent = Encoding.ASCII.GetBytes("%PDF-1 service attachment");

        var submit = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/flights/{flightId}/work-orders",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                workOrder = new
                {
                    type = "Completion",
                    actualFlightNumber = "MOB225",
                    aircraftTypeId = refs.AircraftTypeId,
                    aircraftTailNumber = "HZ-TEST",
                    actualArrivalUtc = now.AddHours(-1),
                    actualDepartureUtc = now.AddHours(1),
                    remarks = "Service attachment",
                    serviceLines = new[]
                    {
                        new
                        {
                            serviceId = refs.ServiceId,
                            performedByStaffMemberIds = new[] { staff.StaffId },
                            fromUtc = now.AddMinutes(-30),
                            toUtc = now.AddMinutes(30),
                            description = "Attached service",
                            attachments = new[]
                            {
                                new
                                {
                                    kind = "Document",
                                    base64Content = Convert.ToBase64String(fileContent),
                                    fileName = "service-report.pdf",
                                    contentType = "application/pdf"
                                }
                            }
                        }
                    },
                    tasks = Array.Empty<object>()
                }
            });
        submit.StatusCode.ShouldBe(HttpStatusCode.Created, await submit.Content.ReadAsStringAsync());
        var created = await submit.Content.ReadFromJsonAsync<MobileWriteResult>();

        var detail = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{created!.WorkOrderId}");
        var serviceLine = detail!.ServiceLines.ShouldHaveSingleItem();
        var attachment = serviceLine.Attachments.ShouldHaveSingleItem();
        attachment.Kind.ShouldBe("Document");
        attachment.OriginalFileName.ShouldBe("service-report.pdf");
        attachment.ContentType.ShouldBe("application/pdf");
        attachment.Size.ShouldBe(fileContent.Length);

        var download = await staff.Client.GetAsync(
            $"{OperationsApiFactory.Base}/work-orders/{created.WorkOrderId}/service-lines/{serviceLine.Id}/attachments/{attachment.Id}");
        download.StatusCode.ShouldBe(HttpStatusCode.OK, await download.Content.ReadAsStringAsync());
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(fileContent);

        var update = await staff.Client.PutAsJsonAsync(
            $"{MobileBase}/work-orders/{created.WorkOrderId}",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                baseRowVersion = detail.RowVersion,
                serviceLineIdentityVersion = 1,
                workOrder = new
                {
                    type = "Completion",
                    actualFlightNumber = "MOB225",
                    aircraftTypeId = refs.AircraftTypeId,
                    aircraftTailNumber = "HZ-TEST",
                    actualArrivalUtc = now.AddHours(-1),
                    actualDepartureUtc = now.AddHours(1),
                    remarks = "Updated without replacing the service",
                    serviceLines = new[]
                    {
                        new
                        {
                            id = (Guid?)serviceLine.Id,
                            serviceId = refs.ServiceId,
                            performedByStaffMemberIds = new[] { staff.StaffId },
                            fromUtc = now.AddMinutes(-25),
                            toUtc = now.AddMinutes(35),
                            description = "Updated attached service",
                            attachments = Array.Empty<object>()
                        }
                    },
                    tasks = Array.Empty<object>()
                }
            });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());

        var updated = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{created.WorkOrderId}");
        var retainedService = updated!.ServiceLines.ShouldHaveSingleItem();
        retainedService.Id.ShouldBe(serviceLine.Id);
        retainedService.Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachment.Id);

        using var delete = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{OperationsApiFactory.Base}/work-orders/{created.WorkOrderId}/service-lines/{retainedService.Id}/attachments/{attachment.Id}");
        delete.Headers.TryAddWithoutValidation("If-Match", updated.RowVersion);
        var deleted = await staff.Client.SendAsync(delete);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        var afterDelete = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{created.WorkOrderId}");
        afterDelete!.ServiceLines.ShouldHaveSingleItem().Attachments.ShouldBeEmpty();
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
    public async Task Mobile_scratch_create_maps_omitted_and_explicit_unknown_customer_to_the_seeded_customer()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(-2);
        var scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(2);
        var omittedMutationId = Guid.NewGuid().ToString();
        var omittedClientFlightId = Guid.NewGuid();
        var omittedWorkOrderBody = CompletionWorkOrderBody(
            refs,
            staff.StaffId,
            remarks: "Operator name was not available.");

        var omittedCustomerResponse = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/work-orders/scratch",
            new
            {
                clientMutationId = omittedMutationId,
                clientFlightId = omittedClientFlightId,
                flightNumber = "MOB310",
                scheduledArrivalUtc,
                scheduledDepartureUtc,
                aircraftTypeId = refs.AircraftTypeId,
                plannedServiceIds = new[] { refs.ServiceId },
                workOrder = omittedWorkOrderBody
            });
        omittedCustomerResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await omittedCustomerResponse.Content.ReadAsStringAsync());
        var omittedCustomer = await omittedCustomerResponse.Content.ReadFromJsonAsync<MobileWriteResult>();

        // Omitted and explicit Unknown are the same semantic request, including for idempotency.
        var normalizedReplayResponse = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/work-orders/scratch",
            new
            {
                clientMutationId = omittedMutationId,
                clientFlightId = omittedClientFlightId,
                customerId = WellKnownMasterDataIds.UnknownCustomer,
                flightNumber = "MOB310",
                scheduledArrivalUtc,
                scheduledDepartureUtc,
                aircraftTypeId = refs.AircraftTypeId,
                plannedServiceIds = new[] { refs.ServiceId },
                workOrder = omittedWorkOrderBody
            });
        normalizedReplayResponse.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await normalizedReplayResponse.Content.ReadAsStringAsync());
        var normalizedReplay = await normalizedReplayResponse.Content.ReadFromJsonAsync<MobileWriteResult>();
        normalizedReplay!.Idempotent.ShouldBeTrue();
        normalizedReplay.WorkOrderId.ShouldBe(omittedCustomer!.WorkOrderId);

        var explicitUnknownResponse = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/work-orders/scratch",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                clientFlightId = Guid.NewGuid(),
                customerId = WellKnownMasterDataIds.UnknownCustomer,
                flightNumber = "MOB311",
                scheduledArrivalUtc,
                scheduledDepartureUtc,
                aircraftTypeId = refs.AircraftTypeId,
                plannedServiceIds = new[] { refs.ServiceId },
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId, remarks: "Unbadged walk-in operator.")
            });
        explicitUnknownResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await explicitUnknownResponse.Content.ReadAsStringAsync());
        var explicitUnknown = await explicitUnknownResponse.Content.ReadFromJsonAsync<MobileWriteResult>();

        var omittedWorkOrder = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{omittedCustomer!.WorkOrderId}");
        var explicitWorkOrder = await staff.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{explicitUnknown!.WorkOrderId}");
        var omittedFlight = await staff.Client.GetFromJsonAsync<MobileFlight>(
            $"{MobileBase}/flights/{omittedCustomer.FlightId}");
        var explicitFlight = await staff.Client.GetFromJsonAsync<MobileFlight>(
            $"{MobileBase}/flights/{explicitUnknown.FlightId}");

        omittedWorkOrder!.CustomerId.ShouldBe(WellKnownMasterDataIds.UnknownCustomer);
        explicitWorkOrder!.CustomerId.ShouldBe(WellKnownMasterDataIds.UnknownCustomer);
        omittedFlight!.CustomerId.ShouldBe(WellKnownMasterDataIds.UnknownCustomer);
        explicitFlight!.CustomerId.ShouldBe(WellKnownMasterDataIds.UnknownCustomer);
    }

    [Fact]
    public async Task Mobile_scratch_create_rejects_missing_remarks_when_customer_is_omitted_or_unknown()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, MobileStaffPermissions);
        var scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(-2);
        var scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(2);

        var omittedCustomerResponse = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/work-orders/scratch",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                clientFlightId = Guid.NewGuid(),
                flightNumber = "MOB312",
                scheduledArrivalUtc,
                scheduledDepartureUtc,
                aircraftTypeId = refs.AircraftTypeId,
                plannedServiceIds = new[] { refs.ServiceId },
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId, remarks: "")
            });

        var explicitUnknownResponse = await staff.Client.PostAsJsonAsync(
            $"{MobileBase}/work-orders/scratch",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                clientFlightId = Guid.NewGuid(),
                customerId = WellKnownMasterDataIds.UnknownCustomer,
                flightNumber = "MOB313",
                scheduledArrivalUtc,
                scheduledDepartureUtc,
                aircraftTypeId = refs.AircraftTypeId,
                plannedServiceIds = new[] { refs.ServiceId },
                workOrder = CompletionWorkOrderBody(refs, staff.StaffId, remarks: "  ")
            });

        omittedCustomerResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        explicitUnknownResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await omittedCustomerResponse.Content.ReadAsStringAsync()).ShouldContain("Remarks");
        (await explicitUnknownResponse.Content.ReadAsStringAsync()).ShouldContain("Remarks");
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
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                // A regular mobile create cannot forge RTR provenance; the server owns this flag.
                workOrder = CompletionWorkOrderBody(refs, author.StaffId, isReturnToRamp: true)
            });
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
                        performedByStaffMemberIds = new[] { author.StaffId },
                        fromUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                        toUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                        description = "Return to ramp"
                    }
                },
                tasks = new[]
                {
                    new
                    {
                        id = (Guid?)null,
                        taskType = "Minor",
                        description = "Ramp inspection",
                        fromUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                        toUtc = DateTimeOffset.UtcNow.AddMinutes(20),
                        employeeIds = new[] { author.StaffId },
                        tools = Array.Empty<object>(),
                        materials = Array.Empty<object>(),
                        generalSupports = Array.Empty<object>()
                    }
                }
            });
        rtr.StatusCode.ShouldBe(HttpStatusCode.OK, await rtr.Content.ReadAsStringAsync());

        var detail = await author.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{submitted.WorkOrderId}");
        detail!.ServiceLines.Count.ShouldBe(2);
        detail.ServiceLines.Count(line => line.IsReturnToRamp).ShouldBe(1);
        detail.ServiceLines.Single(line => line.Description == "Handled").IsReturnToRamp.ShouldBeFalse();
        detail.ServiceLines.Single(line => line.Description == "Return to ramp").IsReturnToRamp.ShouldBeTrue();
        detail.Tasks.ShouldHaveSingleItem().IsReturnToRamp.ShouldBeTrue();

        // The update screen sends the full line collections back. Echoing each source flag must
        // keep the RTR service and task distinguishable after the aggregate applies the edit.
        var update = await author.Client.PutAsJsonAsync(
            $"{MobileBase}/work-orders/{submitted.WorkOrderId}",
            new
            {
                clientMutationId = Guid.NewGuid().ToString(),
                baseRowVersion = detail.RowVersion,
                serviceLineIdentityVersion = 1,
                workOrder = new
                {
                    type = "Completion",
                    actualFlightNumber = "MOB999",
                    aircraftTypeId = refs.AircraftTypeId,
                    aircraftTailNumber = "HZ-TEST",
                    actualArrivalUtc = DateTimeOffset.UtcNow.AddHours(-1),
                    actualDepartureUtc = DateTimeOffset.UtcNow.AddHours(1),
                    remarks = "Updated after return to ramp",
                    serviceLines = detail.ServiceLines.Select(line => new
                        {
                            Id = (Guid?)line.Id,
                            line.ServiceId,
                            PerformedByStaffMemberIds = line.PerformedBy.Select(performer => performer.StaffMemberId).ToArray(),
                            line.FromUtc,
                            line.ToUtc,
                            line.Description,
                            // The regular update route must ignore attempted reclassification.
                            IsReturnToRamp = !line.IsReturnToRamp
                        })
                        .Concat(
                        [
                            new
                            {
                                Id = (Guid?)null,
                                ServiceId = refs.ServiceId,
                                PerformedByStaffMemberIds = new[] { author.StaffId },
                                FromUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                                ToUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                                Description = (string?)"Normal update addition",
                                IsReturnToRamp = true
                            }
                        ]),
                    tasks = detail.Tasks.Select(task => new
                    {
                        task.Id,
                        task.TaskType,
                        task.Description,
                        task.FromUtc,
                        task.ToUtc,
                        employeeIds = task.Employees.Select(employee => employee.StaffMemberId),
                        tools = Array.Empty<object>(),
                        materials = Array.Empty<object>(),
                        generalSupports = Array.Empty<object>(),
                        IsReturnToRamp = false
                    })
                }
            });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());

        var updatedDetail = await author.Client.GetFromJsonAsync<WorkOrderDetail>(
            $"{MobileBase}/work-orders/{submitted.WorkOrderId}");
        updatedDetail!.ServiceLines.Count(line => line.IsReturnToRamp).ShouldBe(1);
        updatedDetail.ServiceLines.Single(line => line.Description == "Return to ramp").IsReturnToRamp.ShouldBeTrue();
        updatedDetail.ServiceLines.Single(line => line.Description == "Handled").IsReturnToRamp.ShouldBeFalse();
        updatedDetail.ServiceLines.Single(line => line.Description == "Normal update addition").IsReturnToRamp.ShouldBeFalse();
        updatedDetail.Tasks.ShouldHaveSingleItem().IsReturnToRamp.ShouldBeTrue();
    }

    // --- Helpers -------------------------------------------------------------------

    private static object CompletionWorkOrderBody(
        MasterDataRefs refs,
        Guid performerId,
        string remarks = "Mobile submission",
        bool isReturnToRamp = false) =>
        CompletionWorkOrderBody(refs, [performerId], remarks, isReturnToRamp);

    private static object CompletionWorkOrderBody(
        MasterDataRefs refs,
        IReadOnlyList<Guid> performerIds,
        string remarks = "Mobile submission",
        bool isReturnToRamp = false) => new
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
                performedByStaffMemberIds = performerIds,
                fromUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                toUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                description = "Handled",
                isReturnToRamp
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
    private sealed record CatalogItem(Guid Id, string Name, string CalculationType);
    private sealed record CatalogCustomer(Guid Id, string? IataCode, string Name);
    private sealed record CatalogAircraftType(Guid Id, string Manufacturer, string Model);

    private sealed record MobileStaffMember(Guid StaffMemberId, string FullName, string EmployeeId);

    private sealed record MobileFlight(
        Guid Id, string FlightNumber, string Status, bool IsPerLanding, bool IsAdHoc,
        Guid CustomerId, string CustomerName,
        List<PlannedService> PlannedServices, WorkOrderDetail? MyWorkOrder,
        bool OtherWorkOrdersExist, string RowVersion,
        bool IsWithinMobileWindow, DateTimeOffset MobileWindowStartsAtUtc, DateTimeOffset MobileWindowEndsAtUtc);

    private sealed record PlannedService(Guid ServiceId, string Name, bool IsAircraftPerLanding);

    private sealed record WorkOrderDetail(
        Guid Id, Guid FlightId, string Type, string Status,
        Guid CustomerId, string CustomerName,
        string? CancellationReason, string? Remarks, List<ServiceLine> ServiceLines, List<TaskLine> Tasks, string RowVersion,
        List<ReturnToRampLine>? ReturnToRamps = null);

    private sealed record ReturnToRampLine(
        Guid Id,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        string? Description,
        List<ServiceLine> ServiceLines,
        List<TaskLine> Tasks);

    private sealed record ServiceLine(
        Guid Id,
        Guid ServiceId,
        string ServiceName,
        List<WorkOrderServiceLinePerformer> PerformedBy,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        string? Description,
        bool IsReturnToRamp,
        List<ServiceLineAttachment>? Attachments = null);

    private sealed record ServiceLineAttachment(
        Guid Id,
        string Kind,
        string OriginalFileName,
        string ContentType,
        long Size);

    private sealed record WorkOrderServiceLinePerformer(
        Guid StaffMemberId,
        string FullName,
        string EmployeeId);

    private sealed record TaskLine(
        Guid Id,
        string TaskType,
        string? Description,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        List<TaskEmployee> Employees,
        List<TaskTool> Tools,
        List<TaskMaterial> Materials,
        List<TaskGeneralSupport> GeneralSupports,
        bool IsReturnToRamp);

    private sealed record TaskTool(
        Guid ToolId,
        string Name,
        string CalculationType,
        decimal? Quantity,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc)
    {
        public Guid ResourceId => ToolId;
    }

    private sealed record TaskMaterial(
        Guid MaterialId,
        string Name,
        string CalculationType,
        decimal? Quantity,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc)
    {
        public Guid ResourceId => MaterialId;
    }

    private sealed record TaskGeneralSupport(
        Guid GeneralSupportId,
        string Name,
        string CalculationType,
        decimal? Quantity,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc)
    {
        public Guid ResourceId => GeneralSupportId;
    }

    private sealed record TaskEmployee(Guid StaffMemberId);

    private sealed record SyncChange(string Table, string Op, string? EntityId, string Audience, DateTimeOffset Version);

    private sealed record MobileWriteResult(Guid WorkOrderId, Guid FlightId, bool Idempotent);
}
