using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Operations.Application.Contracts;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    private static readonly string[] PortalOperatorPermissions =
        ["operations.flights.view", "operations.flights.operate"];

    [Fact]
    public async Task Portal_operations_require_an_eligible_linked_operator_even_with_register_access()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var worker = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var workerClient = worker.Client;
        var dispatcher = await CreateStaffLoginAsync(admin, refs, [.. PortalOperatorPermissions, "operations.flights.view-station"]);
        using var dispatcherClient = dispatcher.Client;
        var reader = await CreateStaffLoginAsync(admin, refs, RegularStaffPermissions);
        using var readerClient = reader.Client;
        using var viewer = await CreatePortalViewerAsync(admin);
        using var anonymous = factory.CreateClient();
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8711", [worker.StaffId, reader.StaffId]);
        var otherStationId = await CreateStationAsync(admin, refs.CountryId);
        var otherFlight = await ScheduleFlightAsync(admin, otherStationId, refs, "8712");
        var detail = await PortalOperationsDetailAsync(workerClient, id);
        detail.CurrentStaffMemberId.ShouldBe(worker.StaffId);
        detail.CanSaveArrival.ShouldBeTrue();
        detail.CanSaveCancellation.ShouldBeTrue();
        var path = $"{Base}/flights/{id}";
        var input = new SaveFlightCancellationRequest(DateTimeOffset.UtcNow, null);

        foreach (var denied in new[] { admin, readerClient, viewer })
        {
            using var register = await denied.GetAsync(path);
            register.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var editor = await denied.GetAsync($"{path}/operations");
            editor.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            using var save = await WritePortalOperationsAsync(denied, id, HttpMethod.Put, "cancellation", input, detail.RowVersion);
            save.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        using var unauthenticated = await anonymous.GetAsync($"{path}/operations");
        unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var dispatcherRegister = await dispatcherClient.GetAsync(path);
        dispatcherRegister.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dispatcherEditor = await PortalOperationsDetailAsync(dispatcherClient, id);
        dispatcherEditor.IsUnassignedSearchFlight.ShouldBeTrue();
        dispatcherEditor.CanSaveArrival.ShouldBeTrue();
        dispatcherEditor.CanSaveCancellation.ShouldBeFalse();
        using var dispatcherCancel = await WritePortalOperationsAsync(dispatcherClient, id, HttpMethod.Put, "cancellation", input, detail.RowVersion);
        dispatcherCancel.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var crossStationEditor = await workerClient.GetAsync($"{Base}/flights/{otherFlight}/operations");
        crossStationEditor.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PortalOperationsDetailAsync(workerClient, id)).RowVersion.ShouldBe(detail.RowVersion);
    }

    [Fact]
    public async Task Portal_cancellation_correction_and_exact_undo_preserve_other_employee_work()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var reporter = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        var worker = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var reporterClient = reporter.Client;
        using var workerClient = worker.Client;
        var now = DateTimeOffset.UtcNow;
        var id = await SchedulePortalFlightAtAsync(admin, refs, "8713", now.AddHours(24), [reporter.StaffId, worker.StaffId]);
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var initial = await PortalOperationsDetailAsync(reporterClient, id);
        var input = new SaveFlightCancellationRequest(now.AddHours(-24), "First cancellation");
        using var savedResponse = await WritePortalOperationsAsync(reporterClient, id, HttpMethod.Put, "cancellation", input, initial.RowVersion);
        savedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await savedResponse.Content.ReadFromJsonAsync<FlightCancellationSavedDto>())!;
        saved.Submission.SubmittedByEmployee.StaffMemberId.ShouldBe(reporter.StaffId);
        using var mobileRead = await reporterClient.GetAsync($"{Base}/flights/mobile/{id}");
        mobileRead.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var canceled = await PortalOperationsDetailAsync(reporterClient, id);
        canceled.CancellationSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(saved.Submission.Id);
        canceled.CanSaveArrival.ShouldBeFalse();
        canceled.CanSaveCancellation.ShouldBeTrue();
        var revertPath = $"cancellation/{saved.Submission.Id}";
        using var otherUndo = await WritePortalOperationsAsync(workerClient, id, HttpMethod.Delete, revertPath, null, saved.RowVersion);
        otherUndo.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var arrival = new SaveFlightArrivalRequest(now.AddMinutes(-30), aircraftId, "HZ-PORTAL", null, null);
        var work = OperationsBatchInput(arrival);
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(reporterClient, id, HttpMethod.Put, "batch", work, saved.RowVersion),
            "Operations.Flight.OwnCancellationSubmitted");
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(workerClient, id, HttpMethod.Put, "batch", work, saved.RowVersion),
            "Operations.Flight.CancellationConfirmationRequired");
        using var workResponse = await WritePortalOperationsAsync(workerClient, id, HttpMethod.Put, "batch",
            OperationsBatchInput(arrival with { AcknowledgeConflictingSubmission = true }), saved.RowVersion);
        workResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var worked = (await workResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        worked.ArrivalSubmissions!.ShouldHaveSingleItem().SubmittedByEmployee.StaffMemberId.ShouldBe(worker.StaffId);
        worked.CanSaveCancellation.ShouldBeFalse();
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(workerClient, id, HttpMethod.Put, "cancellation", input, worked.RowVersion),
            "Operations.Flight.OwnWorkSubmitted");

        var correction = input with { Remarks = "Updated cancellation", AcknowledgeConflictingSubmission = true };
        using var correctionResponse = await WritePortalOperationsAsync(reporterClient, id, HttpMethod.Put, "cancellation", correction, worked.RowVersion);
        correctionResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corrected = (await correctionResponse.Content.ReadFromJsonAsync<FlightCancellationSavedDto>())!;
        corrected.Submission.Id.ShouldBe(saved.Submission.Id);
        corrected.Submission.Remarks.ShouldBe(correction.Remarks);
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(reporterClient, id, HttpMethod.Delete, revertPath, null, saved.RowVersion),
            "General.ConcurrencyConflict");

        using var undo = await WritePortalOperationsAsync(reporterClient, id, HttpMethod.Delete, revertPath, null, corrected.RowVersion);
        undo.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = await PortalOperationsDetailAsync(reporterClient, id);
        restored.CancellationSubmissions!.ShouldBeEmpty();
        restored.ArrivalSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(worked.ArrivalSubmissions!.Single().Id);
        restored.CanSaveArrival.ShouldBeTrue();
        restored.CanSaveCancellation.ShouldBeTrue();

        using var replacement = await WritePortalOperationsAsync(reporterClient, id, HttpMethod.Put, "cancellation", correction, restored.RowVersion);
        replacement.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replaced = (await replacement.Content.ReadFromJsonAsync<FlightCancellationSavedDto>())!;
        replaced.Submission.Id.ShouldNotBe(saved.Submission.Id);
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(reporterClient, id, HttpMethod.Delete, revertPath, null, replaced.RowVersion),
            "Operations.Cancellation.SubmissionChanged");
    }

    [Fact]
    public async Task Portal_operations_save_normal_and_rtr_records_and_read_versioned_media_outside_mobile_window()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var client = actor.Client;
        var now = DateTimeOffset.UtcNow;
        var ata = now.AddDays(-2);
        var id = await SchedulePortalFlightAtAsync(admin, refs, "8714", ata, [actor.StaffId]);
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var initial = await PortalOperationsDetailAsync(client, id);
        var recordId = Guid.NewGuid();
        var bytes = ServicePng();
        var attachment = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Image, "portal.png", "image/png", bytes);
        var service = OperationsBatchClosedService(refs.ServiceId, actor.StaffId, ata.AddMinutes(10), ata.AddMinutes(30))
            with { Attachments = [attachment] };
        var departure = new SaveFlightDepartureRequest(ata.AddHours(1), bytes, ata.AddMinutes(55), "Normal departure");
        using var workResponse = await WritePortalOperationsAsync(client, id, HttpMethod.Put, "batch",
            OperationsBatchInput(new(ata, aircraftId, "HZ-PORTAL", null, null), departure, [new(recordId, service)]),
            initial.RowVersion);
        workResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var work = (await workResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var normalDeparture = work.DepartureSubmissions!.ShouldHaveSingleItem();
        normalDeparture.SubmittedByEmployee.StaffMemberId.ShouldBe(actor.StaffId);
        var storedAttachment = work.Services!.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem();
        storedAttachment.Id.ShouldBe(attachment.Id);
        using var mobileRead = await client.GetAsync($"{Base}/flights/mobile/{id}");
        mobileRead.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var prefix = $"{Base}/flights/{id}/operations";
        var signaturePath = $"{prefix}/departure-signatures/{normalDeparture.Id}" +
            $"?expectedUpdatedAt={VersionTimestamp(normalDeparture.UpdatedAtUtc ?? normalDeparture.CreatedAtUtc)}";
        await AssertPortalMediaAsync(client, signaturePath, bytes);
        using var unlinkedSignatureRead = await admin.GetAsync(signaturePath);
        unlinkedSignatureRead.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var attachmentPath = $"{prefix}/services/{recordId}/attachments/{attachment.Id}";
        await AssertPortalMediaAsync(client,
            $"{attachmentPath}?expectedUploadedAt={VersionTimestamp(storedAttachment.UploadedAtUtc)}", bytes);
        using var staleMedia = await client.GetAsync($"{attachmentPath}?expectedUploadedAt={VersionTimestamp(storedAttachment.UploadedAtUtc.AddSeconds(1))}");
        staleMedia.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        using var removeResponse = await WritePortalOperationsAsync(client, id, HttpMethod.Put, "batch",
            OperationsBatchInput(services: [new(recordId, null)]), work.RowVersion);
        removeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var withoutService = (await removeResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        withoutService.Services!.ShouldBeEmpty();
        withoutService.ArrivalSubmissions!.ShouldHaveSingleItem();
        withoutService.DepartureSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(normalDeparture.Id);

        var occurrenceId = Guid.NewGuid();
        var rtrArrival = ata.AddHours(2);
        var rtrDeparture = rtrArrival.AddHours(1);
        var taskId = Guid.NewGuid();
        var task = OperationsBatchClosedTask(actor.StaffId, rtrArrival.AddMinutes(10), rtrArrival.AddMinutes(30));
        var returning = new SaveFlightReturnToRampRequest(new(rtrArrival, "Return recorded via portal"),
            new(rtrDeparture, "RTR departure", bytes, "rtr.png", "image/png", rtrDeparture), [], [new(taskId, task)]);
        using var rtrResponse = await WritePortalOperationsAsync(client, id, HttpMethod.Put, $"returns-to-ramp/{occurrenceId}",
            returning, withoutService.RowVersion);
        rtrResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var returned = (await rtrResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var occurrence = returned.ReturnToRamps!.ShouldHaveSingleItem();
        occurrence.Id.ShouldBe(occurrenceId);
        occurrence.DepartedAtUtc.ShouldBe(rtrDeparture);
        occurrence.Tasks.ShouldHaveSingleItem().Id.ShouldBe(taskId);
        returned.Tasks!.ShouldBeEmpty();
        returned.DepartureSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(normalDeparture.Id);
        await AssertPortalMediaAsync(client,
            $"{prefix}/returns-to-ramp/{occurrenceId}/signature?expectedUpdatedAt={VersionTimestamp(occurrence.UpdatedAtUtc ?? occurrence.CreatedAtUtc)}", bytes);
    }

    private async Task<HttpClient> CreatePortalViewerAsync(HttpClient admin)
    {
        var roleId = await PostForIdAsync(admin, $"{IdentityBase}/roles", new
        {
            name = $"Operations portal viewer {Guid.NewGuid():N}",
            compatibleUserType = "ViewerOnly",
            permissions = new[] { "operations.flights.view" }
        });
        var email = $"portal-viewer-{Guid.NewGuid():N}@example.com";
        using var invitation = await admin.PostAsJsonAsync($"{IdentityBase}/users/invite",
            new { email, displayName = "Operations portal viewer", roleId });
        invitation.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invitationToken = await factory.GetInvitationTokenAsync(email);
        const string password = "ViewerPass#12345";
        using var activation = await admin.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email, invitationToken, newPassword = password });
        activation.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return await factory.CreateAuthenticatedClientAsync(email, password);
    }

    private static Task<Guid> SchedulePortalFlightAtAsync(HttpClient admin, MasterDataRefs refs, string number,
        DateTimeOffset arrival, IReadOnlyList<Guid> assignedStaff) =>
        PostForIdAsync(admin, $"{Base}/flights", new
        {
            customerId = refs.CustomerId, refs.StationId, refs.OperationTypeId, flightNumber = number,
            scheduledArrivalUtc = arrival, scheduledDepartureUtc = arrival.AddHours(1),
            plannedServiceIds = new[] { refs.ServiceId }, assignedStaffMemberIds = assignedStaff
        });

    private static async Task<FlightDetailDto> PortalOperationsDetailAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/{id}/operations"))!;

    private static async Task<HttpResponseMessage> WritePortalOperationsAsync(HttpClient client, Guid id,
        HttpMethod method, string route, object? input, string version)
    {
        using var request = new HttpRequestMessage(method, $"{Base}/flights/{id}/operations/{route}")
        {
            Content = input is null ? null : JsonContent.Create(input)
        };
        request.Headers.TryAddWithoutValidation("If-Match", version);
        return await client.SendAsync(request);
    }

    private static async Task AssertPortalOperationsConflictAsync(HttpResponseMessage response, string code)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(code);
        }
    }

    private static async Task AssertPortalMediaAsync(HttpClient client, string url, byte[] bytes)
    {
        using var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        response.Headers.CacheControl!.Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
    }

    private static string VersionTimestamp(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));
}
