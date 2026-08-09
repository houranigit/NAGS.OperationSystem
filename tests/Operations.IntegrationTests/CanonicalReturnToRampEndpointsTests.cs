using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Api.Endpoints;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class CanonicalReturnToRampEndpointsTests(OperationsApiFactory factory)
    : IClassFixture<OperationsApiFactory>
{
    private const string Base = OperationsApiFactory.Base;
    private const string MasterDataBase = OperationsApiFactory.MasterDataBase;
    private const string IdentityBase = OperationsApiFactory.IdentityBase;

    private static int _stationCounter;

    private static readonly string[] AuthorPermissions =
    [
        "operations.flights.view",
        "operations.work-orders.view",
        "operations.work-orders.author"
    ];

    [Fact]
    public async Task Canonical_routes_append_two_distinct_grouped_occurrences_to_editable_work_order()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs);
        var now = DateTimeOffset.UtcNow;
        var flightId = await ScheduleFlightAsync(admin, refs, "RTR101", [author.StaffId], now);
        var workOrderId = await SubmitCompletionAsync(author.Client, refs, author.StaffId, flightId, "RTR101", now);
        var attachmentBytes = "%PDF-1.7 canonical RTR"u8.ToArray();

        var firstRequest = ServiceOccurrence(
            refs.ServiceId,
            author.StaffId,
            now.AddMinutes(-20),
            now.AddMinutes(20),
            "First occurrence",
            attachmentBytes);
        var firstResponse = await author.Client.PostAsJsonAsync(
            $"{Base}/flights/{flightId}/return-to-ramps",
            firstRequest);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await firstResponse.Content.ReadAsStringAsync());
        var firstId = await firstResponse.Content.ReadFromJsonAsync<Guid>();

        var secondRequest = TaskOccurrence(
            author.StaffId,
            now.AddMinutes(30),
            now.AddMinutes(55),
            "Second occurrence",
            attachmentBytes);
        var secondResponse = await author.Client.PostAsJsonAsync(
            $"{Base}/work-orders/{workOrderId}/return-to-ramps",
            secondRequest);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await secondResponse.Content.ReadAsStringAsync());
        var secondId = await secondResponse.Content.ReadFromJsonAsync<Guid>();

        firstId.ShouldNotBe(Guid.Empty);
        secondId.ShouldNotBe(Guid.Empty);
        secondId.ShouldNotBe(firstId);

        var detail = await author.Client.GetFromJsonAsync<WorkOrderDetailDto>($"{Base}/work-orders/{workOrderId}");
        detail.ShouldNotBeNull();
        detail!.Status.ShouldBe("Submitted");
        var occurrences = detail.ReturnToRamps.ShouldNotBeNull();
        occurrences.Count.ShouldBe(2);
        occurrences.Select(item => item.Id).ShouldBe([firstId, secondId]);
        occurrences.Select(item => item.Description).ShouldBe(["First occurrence", "Second occurrence"]);

        var first = occurrences[0];
        var second = occurrences[1];
        first.ServiceLines.ShouldHaveSingleItem().IsReturnToRamp.ShouldBeTrue();
        first.Tasks.ShouldBeEmpty();
        second.ServiceLines.ShouldBeEmpty();
        second.Tasks.ShouldHaveSingleItem().IsReturnToRamp.ShouldBeTrue();

        // The top-level collections remain a rolling-client compatibility view. Every canonical
        // nested activity must occur there exactly once, never once per Include branch.
        var nestedServiceIds = occurrences.SelectMany(item => item.ServiceLines).Select(item => item.Id).ToList();
        var nestedTaskIds = occurrences.SelectMany(item => item.Tasks).Select(item => item.Id).ToList();
        nestedServiceIds.Count.ShouldBe(nestedServiceIds.Distinct().Count());
        nestedTaskIds.Count.ShouldBe(nestedTaskIds.Distinct().Count());
        detail.ServiceLines.Where(item => item.IsReturnToRamp).Select(item => item.Id).ShouldBe(nestedServiceIds);
        detail.Tasks.Where(item => item.IsReturnToRamp).Select(item => item.Id).ShouldBe(nestedTaskIds);

        var nestedService = first.ServiceLines.ShouldHaveSingleItem();
        var nestedAttachment = nestedService.Attachments.ShouldNotBeNull().ShouldHaveSingleItem();
        var attachmentResponse = await author.Client.GetAsync(
            $"{Base}/work-orders/{workOrderId}/service-lines/{nestedService.Id}/attachments/{nestedAttachment.Id}");
        attachmentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await attachmentResponse.Content.ReadAsByteArrayAsync()).ShouldBe(attachmentBytes);

        var nestedTask = second.Tasks.ShouldHaveSingleItem();
        var nestedTaskAttachment = nestedTask.Attachments.ShouldNotBeNull().ShouldHaveSingleItem();
        var taskAttachmentResponse = await author.Client.GetAsync(
            $"{Base}/work-orders/{workOrderId}/tasks/{nestedTask.Id}/attachments/{nestedTaskAttachment.Id}");
        taskAttachmentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await taskAttachmentResponse.Content.ReadAsByteArrayAsync()).ShouldBe(attachmentBytes);

        using (var deleteServiceAttachment = new HttpRequestMessage(
                   HttpMethod.Delete,
                   $"{Base}/work-orders/{workOrderId}/service-lines/{nestedService.Id}/attachments/{nestedAttachment.Id}"))
        {
            deleteServiceAttachment.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
            var deleted = await author.Client.SendAsync(deleteServiceAttachment);
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        var afterServiceDelete = await author.Client.GetFromJsonAsync<WorkOrderDetailDto>($"{Base}/work-orders/{workOrderId}");
        afterServiceDelete!.ReturnToRamps![0].ServiceLines.ShouldHaveSingleItem().Attachments.ShouldBeEmpty();

        using (var deleteTaskAttachment = new HttpRequestMessage(
                   HttpMethod.Delete,
                   $"{Base}/work-orders/{workOrderId}/tasks/{nestedTask.Id}/attachments/{nestedTaskAttachment.Id}"))
        {
            deleteTaskAttachment.Headers.TryAddWithoutValidation("If-Match", afterServiceDelete.RowVersion);
            var deleted = await author.Client.SendAsync(deleteTaskAttachment);
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        var afterTaskDelete = await author.Client.GetFromJsonAsync<WorkOrderDetailDto>($"{Base}/work-orders/{workOrderId}");
        afterTaskDelete!.ReturnToRamps![1].Tasks.ShouldHaveSingleItem().Attachments.ShouldBeEmpty();

        var timeline = await author.Client.GetFromJsonAsync<List<WorkOrderTimelineEntryDto>>(
            $"{Base}/work-orders/{workOrderId}/timeline");
        var returnEvents = timeline!.Where(item => item.EventType == "ReturnToRampRecorded").ToList();
        returnEvents.Count.ShouldBe(2);
        returnEvents.ShouldContain(item =>
            item.Details != null && item.Details.Contains(firstId.ToString(), StringComparison.Ordinal));
        returnEvents.ShouldContain(item =>
            item.Details != null && item.Details.Contains(secondId.ToString(), StringComparison.Ordinal));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        (await db.WorkOrderReturnToRamps.AsNoTracking().CountAsync(item => item.WorkOrderId == workOrderId)).ShouldBe(2);
        (await db.WorkOrderServiceLines.AsNoTracking().CountAsync(item =>
            item.WorkOrderId == workOrderId && item.ReturnToRampId != null)).ShouldBe(1);
        (await db.Set<WorkOrderTask>().AsNoTracking().CountAsync(item =>
            item.WorkOrderId == workOrderId && item.ReturnToRampId != null)).ShouldBe(1);
    }

    [Fact]
    public async Task Flight_route_appends_to_the_approved_completion_work_order_after_flight_completion()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs);
        var now = DateTimeOffset.UtcNow;
        var flightId = await ScheduleFlightAsync(admin, refs, "RTR201", [author.StaffId], now);
        var workOrderId = await SubmitCompletionAsync(author.Client, refs, author.StaffId, flightId, "RTR201", now);
        var submitted = await author.Client.GetFromJsonAsync<WorkOrderDetailDto>($"{Base}/work-orders/{workOrderId}");

        using (var approve = new HttpRequestMessage(HttpMethod.Post, $"{Base}/work-orders/{workOrderId}/approve"))
        {
            approve.Headers.TryAddWithoutValidation("If-Match", submitted!.RowVersion);
            var approvalResponse = await admin.SendAsync(approve);
            approvalResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await approvalResponse.Content.ReadAsStringAsync());
        }

        var occurrence = TaskOccurrence(
            author.StaffId,
            now.AddMinutes(65),
            now.AddMinutes(85),
            "After approval",
            "%PDF-1.7 approved RTR"u8.ToArray());
        var response = await admin.PostAsJsonAsync($"{Base}/flights/{flightId}/return-to-ramps", occurrence);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var occurrenceId = await response.Content.ReadFromJsonAsync<Guid>();

        var detail = await admin.GetFromJsonAsync<WorkOrderDetailDto>($"{Base}/work-orders/{workOrderId}");
        detail!.Status.ShouldBe("Approved");
        detail.ApprovalNumber.ShouldNotBeNullOrWhiteSpace();
        var recorded = detail.ReturnToRamps.ShouldNotBeNull().ShouldHaveSingleItem();
        recorded.Id.ShouldBe(occurrenceId);
        recorded.Description.ShouldBe("After approval");
        var nestedTask = recorded.Tasks.ShouldHaveSingleItem();
        nestedTask.IsReturnToRamp.ShouldBeTrue();
        var nestedAttachment = nestedTask.Attachments.ShouldNotBeNull().ShouldHaveSingleItem();

        using (var deleteAttachment = new HttpRequestMessage(
                   HttpMethod.Delete,
                   $"{Base}/work-orders/{workOrderId}/tasks/{nestedTask.Id}/attachments/{nestedAttachment.Id}"))
        {
            deleteAttachment.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
            var deleted = await admin.SendAsync(deleteAttachment);
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        var afterDelete = await admin.GetFromJsonAsync<WorkOrderDetailDto>($"{Base}/work-orders/{workOrderId}");
        afterDelete!.Status.ShouldBe("Approved");
        afterDelete.ReturnToRamps!.ShouldHaveSingleItem().Tasks.ShouldHaveSingleItem().Attachments.ShouldBeEmpty();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        (await db.Flights.AsNoTracking().SingleAsync(item => item.Id == flightId)).Status.ShouldBe(FlightStatus.Completed);
    }

    [Fact]
    public async Task Canonical_routes_reject_invalid_activity_windows_empty_payloads_and_unscoped_authors()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs);
        var unassigned = await CreateStaffLoginAsync(admin, refs);
        var now = DateTimeOffset.UtcNow;
        var flightId = await ScheduleFlightAsync(admin, refs, "RTR301", [author.StaffId], now);

        var scheduledFlightResponse = await author.Client.PostAsJsonAsync(
            $"{Base}/flights/{flightId}/return-to-ramps",
            TaskOccurrence(author.StaffId, now, now.AddMinutes(20), "Too early"));
        scheduledFlightResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await scheduledFlightResponse.Content.ReadAsStringAsync()).ShouldContain("Operations.ReturnToRamp.FlightStatusInvalid");

        var workOrderId = await SubmitCompletionAsync(author.Client, refs, author.StaffId, flightId, "RTR301", now);

        var emptyResponse = await author.Client.PostAsJsonAsync(
            $"{Base}/work-orders/{workOrderId}/return-to-ramps",
            new WorkOrderReturnToRampRequest(null, now, now.AddMinutes(20), null, [], []));
        emptyResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await emptyResponse.Content.ReadAsStringAsync()).ShouldContain("Operations.ReturnToRamp.ActivityRequired");

        var invalidWindowResponse = await author.Client.PostAsJsonAsync(
            $"{Base}/work-orders/{workOrderId}/return-to-ramps",
            new WorkOrderReturnToRampRequest(
                null,
                now.AddMinutes(20),
                now,
                null,
                [],
                [Task(author.StaffId, now.AddMinutes(5), now.AddMinutes(10), "Invalid occurrence")]));
        invalidWindowResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalidWindowResponse.Content.ReadAsStringAsync()).ShouldContain("Operations.ReturnToRamp.WindowInvalid");

        var outsideWindowResponse = await author.Client.PostAsJsonAsync(
            $"{Base}/work-orders/{workOrderId}/return-to-ramps",
            new WorkOrderReturnToRampRequest(
                null,
                now,
                now.AddMinutes(20),
                null,
                [],
                [Task(author.StaffId, now.AddMinutes(10), now.AddMinutes(30), "Outside occurrence")]));
        outsideWindowResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await outsideWindowResponse.Content.ReadAsStringAsync()).ShouldContain("Operations.ReturnToRamp.TaskWindowOutsideOccurrence");

        var forbiddenResponse = await unassigned.Client.PostAsJsonAsync(
            $"{Base}/work-orders/{workOrderId}/return-to-ramps",
            TaskOccurrence(unassigned.StaffId, now, now.AddMinutes(20), "Not assigned"));
        forbiddenResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var detail = await author.Client.GetFromJsonAsync<WorkOrderDetailDto>($"{Base}/work-orders/{workOrderId}");
        detail!.ReturnToRamps.ShouldNotBeNull().ShouldBeEmpty();
    }

    private static WorkOrderReturnToRampRequest ServiceOccurrence(
        Guid serviceId,
        Guid staffId,
        DateTimeOffset from,
        DateTimeOffset to,
        string description,
        byte[] attachment) =>
        new(
            null,
            from,
            to,
            description,
            [
                new WorkOrderServiceLineRequest(
                    serviceId,
                    [staffId],
                    from.AddMinutes(2),
                    to.AddMinutes(-2),
                    "Occurrence service",
                    Attachments:
                    [
                        new WorkOrderServiceLineAttachmentRequest(
                            TaskAttachmentKind.Document,
                            Convert.ToBase64String(attachment),
                            "rtr-report.pdf",
                            "application/pdf")
                    ])
            ],
            []);

    private static WorkOrderReturnToRampRequest TaskOccurrence(
        Guid staffId,
        DateTimeOffset from,
        DateTimeOffset to,
        string description,
        byte[]? attachment = null) =>
        new(
            null,
            from,
            to,
            description,
            [],
            [Task(staffId, from.AddMinutes(2), to.AddMinutes(-2), "Occurrence task", attachment)]);

    private static WorkOrderTaskRequest Task(
        Guid staffId,
        DateTimeOffset from,
        DateTimeOffset to,
        string description,
        byte[]? attachment = null) =>
        new(
            null,
            TaskType.Minor,
            description,
            from,
            to,
            [staffId],
            [],
            [],
            [],
            attachment is null
                ? []
                :
                [
                    new WorkOrderTaskAttachmentRequest(
                        TaskAttachmentKind.Document,
                        Convert.ToBase64String(attachment),
                        "rtr-task-report.pdf",
                        "application/pdf")
                ]);

    private static async Task<Guid> SubmitCompletionAsync(
        HttpClient client,
        MasterDataRefs refs,
        Guid staffId,
        Guid flightId,
        string flightNumber,
        DateTimeOffset now)
    {
        var request = new WorkOrderRequest(
            WorkOrderType.Completion,
            flightNumber,
            refs.AircraftTypeId,
            "HZ-RTR",
            now.AddHours(-1),
            now.AddHours(1),
            null,
            null,
            "Canonical RTR integration test",
            [
                new WorkOrderServiceLineRequest(
                    refs.ServiceId,
                    [staffId],
                    now.AddMinutes(-30),
                    now.AddMinutes(30),
                    "Initial handling")
            ],
            []);
        var response = await client.PostAsJsonAsync($"{Base}/flights/{flightId}/work-orders", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<MasterDataRefs> SetupMasterDataAsync(HttpClient admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var countries = await admin.GetFromJsonAsync<PagedList<CountryItem>>(
            $"{MasterDataBase}/countries?page=1&pageSize=1");
        var countryId = countries!.Items[0].Id;
        var stationId = await PostForIdAsync(admin, $"{MasterDataBase}/stations", new
        {
            iataCode = NextThreeLetterCode(),
            icaoCode = (string?)null,
            name = $"RTR Station {suffix}",
            city = "Riyadh",
            countryId
        });
        var customerId = await PostForIdAsync(admin, $"{MasterDataBase}/customers", new
        {
            iataCode = (string?)null,
            icaoCode = (string?)null,
            name = $"RTR Customer {suffix}",
            countryId,
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = new
            {
                line1 = "1 Airport Road",
                line2 = (string?)null,
                city = "Riyadh",
                region = (string?)null,
                postalCode = (string?)null
            },
            contacts = Array.Empty<object>()
        });
        var operationTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/operation-types", new
        {
            name = $"RTR Transit {suffix}",
            description = (string?)null
        });
        var serviceId = await PostForIdAsync(admin, $"{MasterDataBase}/services", new
        {
            name = $"RTR Service {suffix}",
            description = (string?)null
        });
        var manpowerTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/manpower-types", new
        {
            name = $"RTR Manpower {suffix}",
            description = (string?)null
        });
        var aircraftTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/aircraft-types", new
        {
            manufacturer = "Airbus",
            model = $"A320-{suffix}",
            notes = (string?)null
        });

        var manpowerType = await admin.GetFromJsonAsync<ConcurrencyDetail>(
            $"{MasterDataBase}/manpower-types/{manpowerTypeId}");
        using var allowance = new HttpRequestMessage(
            HttpMethod.Put,
            $"{MasterDataBase}/manpower-types/{manpowerTypeId}/service-allowances")
        {
            Content = JsonContent.Create(new { serviceIds = new[] { serviceId } })
        };
        allowance.Headers.TryAddWithoutValidation("If-Match", manpowerType!.RowVersion);
        (await admin.SendAsync(allowance)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return new MasterDataRefs(
            countryId,
            stationId,
            customerId,
            operationTypeId,
            serviceId,
            manpowerTypeId,
            aircraftTypeId);
    }

    private async Task<StaffLogin> CreateStaffLoginAsync(HttpClient admin, MasterDataRefs refs)
    {
        var roleId = await PostForIdAsync(admin, $"{IdentityBase}/roles", new
        {
            name = $"RTR Author {Guid.NewGuid():N}",
            description = (string?)null,
            compatibleUserType = "StationStaff",
            permissions = AuthorPermissions
        });
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"rtr-author-{suffix}@example.com";
        var staffId = await PostForIdAsync(admin, $"{MasterDataBase}/staff-members", new
        {
            fullName = $"RTR Author {suffix}",
            employeeId = $"RTR-{suffix}",
            email,
            stationId = refs.StationId,
            manpowerTypeId = refs.ManpowerTypeId,
            employmentContract = (object?)null,
            workingDays = (string[]?)null,
            licenses = Array.Empty<object>(),
            portalAccessRoleId = roleId
        });

        var invitation = await factory.GetInvitationTokenAsync(email);
        invitation.ShouldNotBeNull();
        const string password = "StaffPass#12345";
        (await admin.PostAsJsonAsync($"{IdentityBase}/auth/activate", new
        {
            email,
            invitationToken = invitation,
            newPassword = password
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        return new StaffLogin(await factory.CreateAuthenticatedClientAsync(email, password), staffId);
    }

    private static async Task<Guid> ScheduleFlightAsync(
        HttpClient admin,
        MasterDataRefs refs,
        string flightNumber,
        IReadOnlyList<Guid> assignedStaffIds,
        DateTimeOffset now)
    {
        var response = await admin.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId,
            stationId = refs.StationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber,
            scheduledArrivalUtc = now.AddHours(-2),
            scheduledDepartureUtc = now.AddHours(2),
            aircraftTypeId = refs.AircraftTypeId,
            plannedServiceIds = new[] { refs.ServiceId },
            assignedStaffMemberIds = assignedStaffIds
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> PostForIdAsync(HttpClient client, string path, object payload)
    {
        var response = await client.PostAsJsonAsync(path, payload);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {path} failed: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static string NextThreeLetterCode()
    {
        var value = Interlocked.Increment(ref _stationCounter);
        return $"R{(char)('A' + (value / 26) % 26)}{(char)('A' + value % 26)}";
    }

    private sealed record MasterDataRefs(
        Guid CountryId,
        Guid StationId,
        Guid CustomerId,
        Guid OperationTypeId,
        Guid ServiceId,
        Guid ManpowerTypeId,
        Guid AircraftTypeId);

    private sealed record StaffLogin(HttpClient Client, Guid StaffId);
    private sealed record ConcurrencyDetail(string RowVersion);
    private sealed record PagedList<T>(List<T> Items);
    private sealed record CountryItem(Guid Id);
}
