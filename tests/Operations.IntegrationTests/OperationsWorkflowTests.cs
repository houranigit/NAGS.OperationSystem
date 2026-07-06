using System.Net;
using System.Net.Http.Json;
using MasterData.Contracts.Seeding;
using Shouldly;

namespace Operations.IntegrationTests;

/// <summary>
/// End-to-end Operations workflows through the real API: scheduling rules, work-order lifecycle
/// (open → submit → approve → return → re-approve), the approved snapshot on the flight,
/// per-landing vs assigned visibility, staff ownership, and the work-order-first path.
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
        "operations.flights.assign",
        "operations.flights.cancel",
        "operations.work-orders.view",
        "operations.work-orders.author",
        "operations.work-orders.submit"
    ];

    // --- Scheduling rules --------------------------------------------------

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
    public async Task Bulk_schedule_with_per_landing_assigned_staff_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);

        var response = await admin.PostAsJsonAsync($"{Base}/flights/bulk", new
        {
            customerId = refs.CustomerId,
            stationId = refs.StationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber = "NGS103",
            scheduledArrivalTimeUtc = new TimeOnly(10, 0),
            scheduledDepartureTimeUtc = new TimeOnly(12, 0),
            selectedDates = new[] { new DateOnly(2026, 8, 3) },
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
            assignedStaffMemberIds = new[] { refs.StaffMemberId }
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
            flight.Status.ShouldBe("Scheduled");
            flight.ScheduledArrivalUtc.ShouldBe(expectedArrival);
            flight.ScheduledDepartureUtc.ShouldBe(expectedDeparture);
            flight.PlannedServices.ShouldContain(service => service.ServiceId == refs.ServiceId);
            flight.AssignedEmployees.ShouldContain(employee => employee.StaffMemberId == refs.StaffMemberId);
        }
    }

    [Fact]
    public async Task Per_landing_cannot_be_recorded_as_actual_service()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var flightId = await ScheduleFlightAsync(admin, refs, "NGS102");
        var workOrderId = await OpenWorkOrderAsync(admin, flightId);
        var workOrder = await GetWorkOrderAsync(admin, workOrderId);

        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/work-orders/{workOrderId}")
        {
            Content = JsonContent.Create(new
            {
                serviceLines = new[]
                {
                    new
                    {
                        serviceId = WellKnownMasterDataIds.AircraftPerLandingService,
                        origin = "Extra",
                        fromUtc = DateTimeOffset.UtcNow,
                        toUtc = DateTimeOffset.UtcNow.AddHours(1),
                        description = (string?)null,
                        returnToRamp = false,
                        employeeIds = new[] { refs.StaffMemberId }
                    }
                },
                tasks = Array.Empty<object>(),
                actualFlightNumber = (string?)null,
                actualAircraftTypeId = (Guid?)null,
                actualArrivalUtc = (DateTimeOffset?)null,
                actualDepartureUtc = (DateTimeOffset?)null,
                aircraftTailNumber = (string?)null,
                remarks = (string?)null,
                customerSignatureReference = (string?)null
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", workOrder.RowVersion);

        (await admin.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Full lifecycle ------------------------------------------------------

    [Fact]
    public async Task Full_cycle_open_keeps_scheduled_submit_moves_in_progress_approve_captures_return_clears_and_reapproval_renumbers()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var flightId = await ScheduleFlightAsync(admin, refs, "NGS200");

        // Opening a draft work order must NOT change the flight status.
        var workOrderId = await OpenWorkOrderAsync(admin, flightId);
        (await GetFlightAsync(admin, flightId)).Status.ShouldBe("Scheduled");

        // Author the completion: actual aircraft type + flight number + ATA/ATD (actual services optional).
        await UpdateWorkOrderAsync(admin, workOrderId, refs, actualFlightNumber: "NGS200A");

        // Submit → flight becomes InProgress (not PendingReview).
        await SubmitWorkOrderAsync(admin, workOrderId);
        (await GetFlightAsync(admin, flightId)).Status.ShouldBe("InProgress");

        // Approve → flight Completed with the approved values + reference captured.
        await ApproveWorkOrderAsync(admin, workOrderId);
        var completed = await GetFlightAsync(admin, flightId);
        completed.Status.ShouldBe("Completed");
        completed.ApprovedWorkOrder.ShouldNotBeNull();
        completed.ApprovedWorkOrder!.WorkOrderId.ShouldBe(workOrderId);
        completed.ApprovedWorkOrder.ActualFlightNumber.ShouldBe("NGS200A");
        completed.ApprovedWorkOrder.ActualArrivalUtc.ShouldNotBeNull();
        var firstNumber = completed.ApprovedWorkOrder.WorkOrderNumber;
        firstNumber.ShouldNotBeNullOrWhiteSpace();

        var approvedWorkOrder = await GetWorkOrderAsync(admin, workOrderId);
        approvedWorkOrder.Status.ShouldBe("Approved");
        approvedWorkOrder.Number.ShouldBe(firstNumber);

        // No more work orders can be added while the flight is settled.
        (await admin.PostAsJsonAsync($"{Base}/flights/{flightId}/work-orders", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Return the approval → flight snapshot cleared, number wiped, flight back InProgress.
        await ReturnWorkOrderAsync(admin, workOrderId);
        var reverted = await GetFlightAsync(admin, flightId);
        reverted.Status.ShouldBe("InProgress");
        reverted.ApprovedWorkOrder.ShouldBeNull();

        var returnedWorkOrder = await GetWorkOrderAsync(admin, workOrderId);
        returnedWorkOrder.Status.ShouldBe("Returned");
        returnedWorkOrder.Number.ShouldBeNull();

        // Re-approval issues a NEW number (numbers are never reused).
        await SubmitWorkOrderAsync(admin, workOrderId);
        await ApproveWorkOrderAsync(admin, workOrderId);
        var reapproved = await GetFlightAsync(admin, flightId);
        reapproved.Status.ShouldBe("Completed");
        reapproved.ApprovedWorkOrder.ShouldNotBeNull();
        reapproved.ApprovedWorkOrder!.WorkOrderNumber.ShouldNotBe(firstNumber);

        // The timeline records the full history including the cleared snapshot.
        var timeline = await admin.GetFromJsonAsync<List<TimelineEntry>>($"{Base}/flights/{flightId}/timeline");
        timeline.ShouldNotBeNull();
        var eventTypes = timeline!.Select(t => t.EventType).ToList();
        eventTypes.ShouldContain("FlightScheduled");
        eventTypes.ShouldContain("WorkOrderCreated");
        eventTypes.ShouldContain("WorkOrderSubmitted");
        eventTypes.ShouldContain("WorkOrderApproved");
        eventTypes.ShouldContain("FlightCompleted");
        eventTypes.ShouldContain("WorkOrderReturned");
        eventTypes.ShouldContain("ApprovedSnapshotCleared");
        timeline.First(t => t.EventType == "ApprovedSnapshotCleared").WorkOrderNumber.ShouldBe(firstNumber);
    }

    [Fact]
    public async Task Cancellation_flow_cancels_flight_and_captures_cancellation_details()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var flightId = await ScheduleFlightAsync(admin, refs, "NGS300");
        var canceledAt = DateTimeOffset.UtcNow;

        var cancel = await admin.PostAsJsonAsync($"{Base}/flights/{flightId}/cancel",
            new { canceledAtUtc = canceledAt, reason = "Customer canceled" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancellationWorkOrderId = await cancel.Content.ReadFromJsonAsync<Guid>();

        // A submitted cancellation keeps the flight InProgress until approval.
        (await GetFlightAsync(admin, flightId)).Status.ShouldBe("InProgress");

        await ApproveWorkOrderAsync(admin, cancellationWorkOrderId);

        var canceled = await GetFlightAsync(admin, flightId);
        canceled.Status.ShouldBe("Canceled");
        canceled.ApprovedWorkOrder.ShouldNotBeNull();
        canceled.ApprovedWorkOrder!.WorkOrderType.ShouldBe("Cancellation");
        canceled.ApprovedWorkOrder.CanceledAtUtc.ShouldNotBeNull();
        canceled.ApprovedWorkOrder.CancellationReason.ShouldBe("Customer canceled");
    }

    // --- Visibility ----------------------------------------------------------

    [Fact]
    public async Task Non_per_landing_flight_is_visible_only_to_assigned_staff()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (assignedClient, assignedStaffId) = await CreateStaffLoginAsync(admin, refs);
        var (unassignedClient, _) = await CreateStaffLoginAsync(admin, refs);

        var flightId = await ScheduleFlightAsync(admin, refs, "NGS400", assignedStaffIds: [assignedStaffId]);

        // Assigned staff sees the flight; unassigned station staff does not.
        (await assignedClient.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await unassignedClient.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var assignedList = await assignedClient.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        assignedList!.Items.ShouldContain(f => f.Id == flightId);
        var unassignedList = await unassignedClient.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        unassignedList!.Items.ShouldNotContain(f => f.Id == flightId);

        // The unassigned staff member cannot author a work order for it either.
        (await unassignedClient.PostAsJsonAsync($"{Base}/flights/{flightId}/work-orders", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Per_landing_flight_is_visible_station_wide()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (staffClient, _) = await CreateStaffLoginAsync(admin, refs);

        var flightId = await ScheduleFlightAsync(admin, refs, "NGS401",
            plannedServiceIds: [WellKnownMasterDataIds.AircraftPerLandingService]);

        // Not assigned, but the flight is Per-Landing → station-wide visibility and authoring.
        (await staffClient.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await staffClient.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        list!.Items.ShouldContain(f => f.Id == flightId && f.IsPerLanding);

        (await staffClient.PostAsJsonAsync($"{Base}/flights/{flightId}/work-orders", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
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

    // --- Ownership -----------------------------------------------------------

    [Fact]
    public async Task Staff_cannot_touch_another_staff_members_work_order_but_can_author_their_own()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (staffAClient, staffAId) = await CreateStaffLoginAsync(admin, refs);
        var (staffBClient, staffBId) = await CreateStaffLoginAsync(admin, refs);

        var flightId = await ScheduleFlightAsync(admin, refs, "NGS500", assignedStaffIds: [staffAId, staffBId]);

        // Staff A opens their work order.
        var openA = await staffAClient.PostAsJsonAsync($"{Base}/flights/{flightId}/work-orders", new { });
        openA.StatusCode.ShouldBe(HttpStatusCode.Created);
        var workOrderA = await openA.Content.ReadFromJsonAsync<Guid>();

        // Staff A cannot open a second active work order for the same flight.
        (await staffAClient.PostAsJsonAsync($"{Base}/flights/{flightId}/work-orders", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Staff B cannot update or submit Staff A's work order.
        var detailA = await GetWorkOrderAsync(staffAClient, workOrderA);
        detailA.OwnerStaffMemberId.ShouldBe(staffAId);

        var foreignUpdate = new HttpRequestMessage(HttpMethod.Put, $"{Base}/work-orders/{workOrderA}")
        {
            Content = JsonContent.Create(EmptyWorkOrderUpdateBody())
        };
        foreignUpdate.Headers.TryAddWithoutValidation("If-Match", detailA.RowVersion);
        (await staffBClient.SendAsync(foreignUpdate)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var foreignSubmit = new HttpRequestMessage(HttpMethod.Post, $"{Base}/work-orders/{workOrderA}/submit");
        foreignSubmit.Headers.TryAddWithoutValidation("If-Match", detailA.RowVersion);
        (await staffBClient.SendAsync(foreignSubmit)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Staff B opens their OWN work order on the same flight (multiple per flight allowed).
        var openB = await staffBClient.PostAsJsonAsync($"{Base}/flights/{flightId}/work-orders", new { });
        openB.StatusCode.ShouldBe(HttpStatusCode.Created);
        var workOrderB = await openB.Content.ReadFromJsonAsync<Guid>();

        // Both staff submit; two submitted work orders coexist while the flight is InProgress.
        await SubmitWorkOrderAsync(staffAClient, workOrderA);
        await SubmitWorkOrderAsync(staffBClient, workOrderB);

        var flight = await GetFlightAsync(admin, flightId);
        flight.Status.ShouldBe("InProgress");
        flight.WorkOrders.Count(w => w.Status == "Submitted").ShouldBe(2);
    }

    // --- Work-order-first ------------------------------------------------------

    [Fact]
    public async Task Work_order_first_creates_flight_and_work_order_with_supplied_fields()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (staffClient, staffId) = await CreateStaffLoginAsync(admin, refs);
        var sta = DateTimeOffset.UtcNow.AddHours(-1);

        var create = await staffClient.PostAsJsonAsync($"{Base}/flights/ad-hoc", new
        {
            customerId = refs.CustomerId,
            operationTypeId = WellKnownMasterDataIds.AdHocOperationType,
            flightNumber = "ADH100",
            scheduledArrivalUtc = sta,
            scheduledDepartureUtc = sta.AddHours(2),
            aircraftTypeId = refs.AircraftTypeId,
            plannedServiceIds = new[] { refs.ServiceId },
            acknowledgeDuplicates = false,
            isCancellation = false,
            cancellationAtUtc = (DateTimeOffset?)null,
            cancellationReason = (string?)null,
            actualFlightNumber = "ADH100A",
            actualAircraftTypeId = refs.AircraftTypeId,
            aircraftTailNumber = "HZ-AK9",
            actualArrivalUtc = sta,
            actualDepartureUtc = sta.AddHours(1),
            serviceLines = Array.Empty<object>(),
            tasks = Array.Empty<object>(),
            remarks = "Walk-up flight",
            customerSignatureReference = (string?)null
        });
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await create.Content.ReadFromJsonAsync<AdHocResult>();
        result.ShouldNotBeNull();

        // The flight exists (InProgress) and the draft work order carries the supplied actual fields.
        var flight = await GetFlightAsync(staffClient, result!.FlightId);
        flight.Status.ShouldBe("InProgress");
        flight.WorkOrders.ShouldContain(w => w.Id == result.WorkOrderId);

        var workOrder = await GetWorkOrderAsync(staffClient, result.WorkOrderId);
        workOrder.Status.ShouldBe("Draft");
        workOrder.OwnerStaffMemberId.ShouldBe(staffId);
        workOrder.FlightNumber.ShouldBe("ADH100A");
        workOrder.AircraftTailNumber.ShouldBe("HZ-AK9");
        workOrder.ActualArrivalUtc.ShouldNotBeNull();

        // The flight appears on the list AND the calendar.
        var list = await staffClient.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        list!.Items.ShouldContain(f => f.Id == result.FlightId);

        var calendar = await staffClient.GetFromJsonAsync<List<CalendarFlight>>(
            $"{Base}/flights/calendar?fromUtc={Uri.EscapeDataString(sta.AddDays(-1).ToString("O"))}&toUtc={Uri.EscapeDataString(sta.AddDays(1).ToString("O"))}");
        calendar!.ShouldContain(f => f.Id == result.FlightId);

        // The same flight then flows through the normal review cycle.
        await SubmitWorkOrderAsync(staffClient, result.WorkOrderId);
        await ApproveWorkOrderAsync(admin, result.WorkOrderId);
        (await GetFlightAsync(admin, result.FlightId)).Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task Work_order_first_without_planned_services_is_rejected_unless_cancellation()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var (staffClient, _) = await CreateStaffLoginAsync(admin, refs);
        var sta = DateTimeOffset.UtcNow;

        object Body(bool isCancellation) => new
        {
            customerId = refs.CustomerId,
            operationTypeId = WellKnownMasterDataIds.AdHocOperationType,
            flightNumber = $"ADH{Random.Shared.Next(200, 999)}",
            scheduledArrivalUtc = sta,
            scheduledDepartureUtc = sta.AddHours(1),
            aircraftTypeId = (Guid?)null,
            plannedServiceIds = Array.Empty<Guid>(),
            acknowledgeDuplicates = true,
            isCancellation,
            cancellationAtUtc = isCancellation ? sta : (DateTimeOffset?)null,
            cancellationReason = (string?)null,
            actualFlightNumber = (string?)null,
            actualAircraftTypeId = (Guid?)null,
            aircraftTailNumber = (string?)null,
            actualArrivalUtc = (DateTimeOffset?)null,
            actualDepartureUtc = (DateTimeOffset?)null,
            serviceLines = Array.Empty<object>(),
            tasks = Array.Empty<object>(),
            remarks = (string?)null,
            customerSignatureReference = (string?)null
        };

        (await staffClient.PostAsJsonAsync($"{Base}/flights/ad-hoc", Body(isCancellation: false)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await staffClient.PostAsJsonAsync($"{Base}/flights/ad-hoc", Body(isCancellation: true)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Helpers ---------------------------------------------------------------

    private sealed record MasterDataRefs(
        Guid CountryId, Guid StationId, Guid CustomerId, Guid OperationTypeId,
        Guid ServiceId, Guid AircraftTypeId, Guid ManpowerTypeId, Guid StaffMemberId, Guid StaffRoleId);

    private async Task<MasterDataRefs> SetupMasterDataAsync(HttpClient admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // The ISO country baseline is seeded on startup; reuse an existing country.
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

    /// <summary>Creates a staff member at the refs station with portal access and returns a logged-in client.</summary>
    private async Task<(HttpClient Client, Guid StaffId)> CreateStaffLoginAsync(HttpClient admin, MasterDataRefs refs)
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
            portalAccessRoleId = refs.StaffRoleId
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
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> OpenWorkOrderAsync(HttpClient client, Guid flightId)
    {
        var response = await client.PostAsJsonAsync($"{Base}/flights/{flightId}/work-orders", new { });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task UpdateWorkOrderAsync(HttpClient client, Guid workOrderId, MasterDataRefs refs, string actualFlightNumber)
    {
        var detail = await GetWorkOrderAsync(client, workOrderId);
        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/work-orders/{workOrderId}")
        {
            Content = JsonContent.Create(new
            {
                serviceLines = Array.Empty<object>(),
                tasks = Array.Empty<object>(),
                actualFlightNumber,
                actualAircraftTypeId = refs.AircraftTypeId,
                actualArrivalUtc = DateTimeOffset.UtcNow.AddHours(2),
                actualDepartureUtc = DateTimeOffset.UtcNow.AddHours(3),
                aircraftTailNumber = "HZ-AK1",
                remarks = "Completed without issues",
                customerSignatureReference = (string?)null
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static object EmptyWorkOrderUpdateBody() => new
    {
        serviceLines = Array.Empty<object>(),
        tasks = Array.Empty<object>(),
        actualFlightNumber = (string?)null,
        actualAircraftTypeId = (Guid?)null,
        actualArrivalUtc = (DateTimeOffset?)null,
        actualDepartureUtc = (DateTimeOffset?)null,
        aircraftTailNumber = (string?)null,
        remarks = (string?)null,
        customerSignatureReference = (string?)null
    };

    private static async Task SubmitWorkOrderAsync(HttpClient client, Guid workOrderId) =>
        await SendLifecycleActionAsync(client, workOrderId, "submit");

    private static async Task ApproveWorkOrderAsync(HttpClient client, Guid workOrderId) =>
        await SendLifecycleActionAsync(client, workOrderId, "approve");

    private static async Task ReturnWorkOrderAsync(HttpClient client, Guid workOrderId) =>
        await SendLifecycleActionAsync(client, workOrderId, "return");

    private static async Task SendLifecycleActionAsync(HttpClient client, Guid workOrderId, string action)
    {
        var detail = await GetWorkOrderAsync(client, workOrderId);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/work-orders/{workOrderId}/{action}");
        request.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, $"'{action}' on work order {workOrderId} failed");
    }

    private static async Task<FlightDetail> GetFlightAsync(HttpClient client, Guid id)
    {
        var flight = await client.GetFromJsonAsync<FlightDetail>($"{Base}/flights/{id}");
        flight.ShouldNotBeNull();
        return flight!;
    }

    private static async Task<WorkOrderDetail> GetWorkOrderAsync(HttpClient client, Guid id)
    {
        var workOrder = await client.GetFromJsonAsync<WorkOrderDetail>($"{Base}/work-orders/{id}");
        workOrder.ShouldNotBeNull();
        return workOrder!;
    }

    private static async Task<Guid> PostForIdAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {path} failed: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    // Unique IATA generator (fresh DB per test class; a shared counter avoids collisions across tests).
    private static string NextThreeLetterCode()
    {
        var n = Interlocked.Increment(ref _stationCounter);
        return $"Q{(char)('A' + (n / 26) % 26)}{(char)('A' + n % 26)}";
    }

    // --- Response mirrors -------------------------------------------------------

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);

    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);

    private sealed record FlightListItem(Guid Id, string FlightNumber, string Status, bool IsPerLanding);

    private sealed record CalendarFlight(Guid Id, string FlightNumber, string Status);

    private sealed record ApprovedWorkOrderBody(
        Guid WorkOrderId, string WorkOrderNumber, string WorkOrderType, string ActualFlightNumber,
        DateTimeOffset? ActualArrivalUtc, DateTimeOffset? ActualDepartureUtc,
        DateTimeOffset? CanceledAtUtc, string? CancellationReason);

    private sealed record WorkOrderSummary(Guid Id, string Type, string Status, string? Number, Guid? OwnerStaffMemberId);

    private sealed record PlannedService(Guid ServiceId, string Name, bool IsAircraftPerLanding);

    private sealed record AssignedEmployee(Guid StaffMemberId, string FullName, string EmployeeId);

    private sealed record FlightDetail(
        Guid Id, string FlightNumber, string Status, bool IsPerLanding,
        DateTimeOffset ScheduledArrivalUtc, DateTimeOffset ScheduledDepartureUtc,
        List<PlannedService> PlannedServices, List<AssignedEmployee> AssignedEmployees,
        ApprovedWorkOrderBody? ApprovedWorkOrder, List<WorkOrderSummary> WorkOrders, string RowVersion);

    private sealed record WorkOrderDetail(
        Guid Id, Guid FlightId, string Type, string Status, string? Number,
        Guid? OwnerStaffMemberId, string? OwnerName, string FlightNumber,
        string? AircraftTailNumber, DateTimeOffset? ActualArrivalUtc, DateTimeOffset? ActualDepartureUtc,
        string RowVersion);

    private sealed record TimelineEntry(
        Guid Id, string EventType, DateTimeOffset OccurredAtUtc, string? ActorName,
        Guid? WorkOrderId, string? WorkOrderNumber, string? Details);

    private sealed record AdHocResult(Guid FlightId, Guid WorkOrderId);
}
