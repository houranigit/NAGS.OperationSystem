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
    public async Task Portal_operations_allow_administrator_reads_and_require_a_linked_operator_to_write()
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

        foreach (var denied in new[] { readerClient, viewer })
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
        var adminView = await PortalOperationsDetailAsync(admin, id);
        AssertReadOnlyPortalOperations(adminView);
        var adminCrossStationView = await PortalOperationsDetailAsync(admin, otherFlight);
        adminCrossStationView.Id.ShouldBe(otherFlight);
        AssertReadOnlyPortalOperations(adminCrossStationView);
        using var adminCreateCancellation = await WritePortalOperationsAsync(admin, otherFlight, HttpMethod.Put, "cancellation",
            input, adminCrossStationView.RowVersion);
        adminCreateCancellation.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var adminMobileRead = await admin.GetAsync($"{Base}/flights/mobile/{id}");
        adminMobileRead.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
        var adminView = await PortalOperationsDetailAsync(admin, id);
        AssertReadOnlyPortalOperations(adminView);
        adminView.CancellationSubmissions!.ShouldHaveSingleItem().ShouldBe(saved.Submission);
        using var adminCorrection = await WritePortalOperationsAsync(admin, id, HttpMethod.Put, "cancellation",
            input with { Remarks = "Administrator correction" }, saved.RowVersion);
        adminCorrection.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var adminUndo = await WritePortalOperationsAsync(admin, id, HttpMethod.Delete, revertPath, null, saved.RowVersion);
        adminUndo.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

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
        await AssertPortalMediaAsync(admin, signaturePath, bytes);
        var attachmentPath = $"{prefix}/services/{recordId}/attachments/{attachment.Id}";
        await AssertPortalMediaAsync(client,
            $"{attachmentPath}?expectedUploadedAt={VersionTimestamp(storedAttachment.UploadedAtUtc)}", bytes);
        await AssertPortalMediaAsync(admin,
            $"{attachmentPath}?expectedUploadedAt={VersionTimestamp(storedAttachment.UploadedAtUtc)}", bytes);
        using var staleMedia = await admin.GetAsync($"{attachmentPath}?expectedUploadedAt={VersionTimestamp(storedAttachment.UploadedAtUtc.AddSeconds(1))}");
        staleMedia.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        var adminWork = await PortalOperationsDetailAsync(admin, id);
        AssertReadOnlyPortalOperations(adminWork);
        adminWork.ArrivalSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(work.ArrivalSubmissions!.Single().Id);
        adminWork.DepartureSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(normalDeparture.Id);
        var adminService = adminWork.Services!.ShouldHaveSingleItem();
        adminService.Id.ShouldBe(recordId);
        adminService.CanEdit.ShouldBeFalse();
        adminService.CanSetEnd.ShouldBeFalse();
        adminService.CanDelete.ShouldBeFalse();
        using var adminBatch = await WritePortalOperationsAsync(admin, id, HttpMethod.Put, "batch",
            OperationsBatchInput(services: [new(recordId, service with { Notes = "Administrator correction" })]), adminWork.RowVersion);
        adminBatch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

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
        var taskAttachment = attachment with { Id = Guid.NewGuid(), FileName = "rtr-task.png" };
        var task = OperationsBatchClosedTask(actor.StaffId, rtrArrival.AddMinutes(10), rtrArrival.AddMinutes(30)) with
        {
            Notes = "RTR inspection notes",
            Performers =
            [
                new(Guid.NewGuid(), actor.StaffId, rtrArrival.AddMinutes(10), rtrArrival.AddMinutes(15)),
                new(Guid.NewGuid(), actor.StaffId, rtrArrival.AddMinutes(20), rtrArrival.AddMinutes(30))
            ],
            Attachments = [taskAttachment]
        };
        var returning = new SaveFlightReturnToRampRequest(new(rtrArrival, "Return recorded via portal"),
            new(rtrDeparture, "RTR departure", bytes, "rtr.png", "image/png", rtrDeparture), [], [new(taskId, task)]);
        using var rtrResponse = await WritePortalOperationsAsync(client, id, HttpMethod.Put, $"returns-to-ramp/{occurrenceId}",
            returning, withoutService.RowVersion);
        rtrResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var returned = (await rtrResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var occurrence = returned.ReturnToRamps!.ShouldHaveSingleItem();
        occurrence.Id.ShouldBe(occurrenceId);
        occurrence.DepartedAtUtc.ShouldBe(rtrDeparture);
        returned.Tasks!.ShouldBeEmpty();
        returned.DepartureSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(normalDeparture.Id);
        await AssertPortalMediaAsync(client,
            $"{prefix}/returns-to-ramp/{occurrenceId}/signature?expectedUpdatedAt={VersionTimestamp(occurrence.UpdatedAtUtc ?? occurrence.CreatedAtUtc)}", bytes);
        await AssertPortalMediaAsync(admin,
            $"{prefix}/returns-to-ramp/{occurrenceId}/signature?expectedUpdatedAt={VersionTimestamp(occurrence.UpdatedAtUtc ?? occurrence.CreatedAtUtc)}", bytes);

        var adminRtr = (await PortalOperationsDetailAsync(admin, id)).ReturnToRamps!.ShouldHaveSingleItem();
        adminRtr.Id.ShouldBe(occurrenceId);
        adminRtr.CanEdit.ShouldBeFalse();
        var adminTask = adminRtr.Tasks.ShouldHaveSingleItem();
        adminTask.Id.ShouldBe(taskId);
        adminTask.CanEdit.ShouldBeFalse();
        adminTask.CanSetEnd.ShouldBeFalse();
        adminTask.CanDelete.ShouldBeFalse();
        using var adminRtrSave = await WritePortalOperationsAsync(admin, id, HttpMethod.Put, $"returns-to-ramp/{occurrenceId}",
            returning with { Arrival = returning.Arrival with { Remarks = "Administrator correction" } }, returned.RowVersion);
        adminRtrSave.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var reloaded = await PortalOperationsDetailAsync(client, id);
        var storedTask = reloaded.ReturnToRamps!.ShouldHaveSingleItem().Tasks.ShouldHaveSingleItem();
        storedTask.Id.ShouldBe(taskId);
        storedTask.Notes.ShouldBe(task.Notes);
        storedTask.Performers.OrderBy(period => period.StartUtc)
            .Select(period => (period.Id, period.StaffMemberId, period.StartUtc, period.EndUtc))
            .ShouldBe(task.Performers.Select(period => (period.Id, period.StaffMemberId, period.StartUtc, period.EndUtc)));
        var storedTaskAttachment = storedTask.Attachments.ShouldHaveSingleItem();
        storedTaskAttachment.Id.ShouldBe(taskAttachment.Id);
        await AssertPortalMediaAsync(client,
            $"{prefix}/tasks/{taskId}/attachments/{taskAttachment.Id}?expectedUploadedAt={VersionTimestamp(storedTaskAttachment.UploadedAtUtc)}", bytes);
        await AssertPortalMediaAsync(admin,
            $"{prefix}/tasks/{taskId}/attachments/{taskAttachment.Id}?expectedUploadedAt={VersionTimestamp(storedTaskAttachment.UploadedAtUtc)}", bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Portal_only_task_submitter_can_delete_while_assigned_colleague_can_update(bool returnToRamp)
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var owner = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        var editor = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var ownerClient = owner.Client;
        using var editorClient = editor.Client;
        var ata = DateTimeOffset.UtcNow.AddHours(-3);
        var id = await SchedulePortalFlightAtAsync(admin, refs, returnToRamp ? "8716" : "8715", ata, [owner.StaffId, editor.StaffId]);
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var initial = await PortalOperationsDetailAsync(ownerClient, id);
        var departure = returnToRamp ? new SaveFlightDepartureRequest(ata.AddHours(1), null, null, null) : null;
        using var actualsResponse = await WritePortalOperationsAsync(ownerClient, id, HttpMethod.Put, "batch",
            OperationsBatchInput(new(ata, aircraftId, "HZ-PORTAL", null, null), departure), initial.RowVersion);
        actualsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var actuals = (await actualsResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;

        var rtrArrival = ata.AddHours(2);
        var route = returnToRamp ? $"returns-to-ramp/{Guid.NewGuid()}" : "batch";
        var taskStart = returnToRamp ? rtrArrival.AddMinutes(10) : ata.AddMinutes(10);
        var closedTask = OperationsBatchClosedTask(owner.StaffId, taskStart, taskStart.AddMinutes(20));
        var first = new FlightTaskBatchChange(Guid.NewGuid(), closedTask with { EndUtc = null });
        var second = new FlightTaskBatchChange(Guid.NewGuid(),
            OperationsBatchClosedTask(owner.StaffId, taskStart.AddMinutes(20), taskStart.AddMinutes(40)));
        using var created = await WriteTasksAsync(ownerClient, actuals.RowVersion, first, second);
        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        var colleagueView = await PortalOperationsDetailAsync(editorClient, id);
        var original = ReadTasks(colleagueView).Single(task => task.Id == first.RecordId);
        original.CanEdit.ShouldBeTrue();
        original.CanSetEnd.ShouldBeTrue();
        original.CanDelete.ShouldBeFalse();
        var removeFirst = new FlightTaskBatchChange(first.RecordId, null);
        using var denied = await WriteTasksAsync(editorClient, colleagueView.RowVersion, removeFirst, second);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("Operations.FlightTask.DeleteReadOnly");

        var correction = closedTask with { Notes = "Corrected and closed by assigned colleague" };
        using var corrected = await WriteTasksAsync(editorClient, colleagueView.RowVersion, first with { Input = correction }, second);
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var correctedTask = ReadTasks((await corrected.Content.ReadFromJsonAsync<FlightDetailDto>())!).Single(task => task.Id == first.RecordId);
        correctedTask.Notes.ShouldBe(correction.Notes);
        correctedTask.EndUtc.ShouldBe(correction.EndUtc);
        correctedTask.CreatedByUserId.ShouldBe(original.CreatedByUserId);
        correctedTask.CanDelete.ShouldBeFalse();

        var ownerView = await PortalOperationsDetailAsync(ownerClient, id);
        ReadTasks(ownerView).Single(task => task.Id == first.RecordId).CanDelete.ShouldBeTrue();
        using var removed = await WriteTasksAsync(ownerClient, ownerView.RowVersion, removeFirst, second);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReadTasks((await removed.Content.ReadFromJsonAsync<FlightDetailDto>())!).ShouldHaveSingleItem().Id.ShouldBe(second.RecordId);

        var mobileView = await OperationsBatchDetailAsync(editorClient, id);
        ReadTasks(mobileView).ShouldHaveSingleItem().CanDelete.ShouldBeTrue();
        using var mobileDelete = await RemoveSecondOnMobileAsync(mobileView.RowVersion);
        mobileDelete.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReadTasks(await OperationsBatchDetailAsync(editorClient, id)).ShouldBeEmpty();

        IReadOnlyList<FlightTaskDto> ReadTasks(FlightDetailDto detail) =>
            returnToRamp ? detail.ReturnToRamps!.Single().Tasks : detail.Tasks!;

        SaveFlightReturnToRampRequest RtrInput(IEnumerable<FlightTaskBatchChange> changes) =>
            new(new(rtrArrival), Services: [], Tasks: changes.Where(change => change.Input is not null)
                .Select(change => new ReturnToRampTaskChange(change.RecordId, change.Input!)).ToArray());

        Task<HttpResponseMessage> WriteTasksAsync(HttpClient client, string version, params FlightTaskBatchChange[] changes) =>
            WritePortalOperationsAsync(client, id, HttpMethod.Put, route,
                returnToRamp ? RtrInput(changes) : OperationsBatchInput(tasks: changes), version);

        async Task<HttpResponseMessage> RemoveSecondOnMobileAsync(string version)
        {
            if (!returnToRamp)
                return await SendOperationsBatchAsync(editorClient, id, OperationsBatchInput(tasks: [new(second.RecordId, null)]), version);

            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/{route}")
            {
                Content = JsonContent.Create(RtrInput([]))
            };
            request.Headers.TryAddWithoutValidation("If-Match", version);
            return await editorClient.SendAsync(request);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Portal_only_service_submitter_can_delete_while_assigned_colleague_can_update(bool returnToRamp)
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var owner = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        var editor = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var ownerClient = owner.Client;
        using var editorClient = editor.Client;
        var ata = DateTimeOffset.UtcNow.AddHours(-3);
        var id = await SchedulePortalFlightAtAsync(admin, refs, returnToRamp ? "8718" : "8717", ata, [owner.StaffId, editor.StaffId]);
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var initial = await PortalOperationsDetailAsync(ownerClient, id);
        var departure = returnToRamp ? new SaveFlightDepartureRequest(ata.AddHours(1), null, null, null) : null;
        using var actualsResponse = await WritePortalOperationsAsync(ownerClient, id, HttpMethod.Put, "batch",
            OperationsBatchInput(new(ata, aircraftId, "HZ-PORTAL", null, null), departure), initial.RowVersion);
        actualsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var actuals = (await actualsResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;

        var rtrArrival = ata.AddHours(2);
        var route = returnToRamp ? $"returns-to-ramp/{Guid.NewGuid()}" : "batch";
        var start = (returnToRamp ? rtrArrival : ata).AddMinutes(10);
        var closedService = OperationsBatchClosedService(refs.ServiceId, owner.StaffId, start, start.AddMinutes(20));
        var serviceId = Guid.NewGuid();
        using var created = await WriteServicesAsync(ownerClient, actuals.RowVersion, new FlightServiceBatchChange(serviceId, closedService with { EndUtc = null }));
        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        var colleagueView = await PortalOperationsDetailAsync(editorClient, id);
        var original = ReadServices(colleagueView).ShouldHaveSingleItem();
        original.CanEdit.ShouldBeTrue();
        original.CanSetEnd.ShouldBeTrue();
        original.CanDelete.ShouldBeFalse();
        var correction = closedService with { Notes = "Corrected and closed by assigned colleague" };
        using var corrected = await WriteServicesAsync(editorClient, colleagueView.RowVersion, new FlightServiceBatchChange(serviceId, correction));
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var correctedDetail = (await corrected.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var correctedService = ReadServices(correctedDetail).ShouldHaveSingleItem();
        correctedService.Notes.ShouldBe(correction.Notes);
        correctedService.EndUtc.ShouldBe(correction.EndUtc);
        correctedService.CreatedByUserId.ShouldBe(original.CreatedByUserId);
        correctedService.CanDelete.ShouldBeFalse();

        var remove = new FlightServiceBatchChange(serviceId, null);
        using var denied = await WriteServicesAsync(editorClient, correctedDetail.RowVersion, remove);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("Operations.FlightService.DeleteReadOnly");

        var ownerView = await PortalOperationsDetailAsync(ownerClient, id);
        ownerView.RowVersion.ShouldBe(correctedDetail.RowVersion);
        ReadServices(ownerView).ShouldHaveSingleItem().CanDelete.ShouldBeTrue();
        var second = new FlightServiceBatchChange(Guid.NewGuid(),
            OperationsBatchClosedService(refs.ServiceId, owner.StaffId, start.AddMinutes(20), start.AddMinutes(40)));
        using var removed = await WriteServicesAsync(ownerClient, ownerView.RowVersion, remove, second);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReadServices((await removed.Content.ReadFromJsonAsync<FlightDetailDto>())!).ShouldHaveSingleItem().Id.ShouldBe(second.RecordId);

        var mobileView = await OperationsBatchDetailAsync(editorClient, id);
        ReadServices(mobileView).ShouldHaveSingleItem().CanDelete.ShouldBeTrue();
        using var mobileDelete = await RemoveSecondOnMobileAsync(mobileView.RowVersion);
        mobileDelete.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReadServices(await OperationsBatchDetailAsync(editorClient, id)).ShouldBeEmpty();

        IReadOnlyList<FlightServiceDto> ReadServices(FlightDetailDto detail) =>
            returnToRamp ? detail.ReturnToRamps!.Single().Services : detail.Services!;

        SaveFlightReturnToRampRequest RtrInput(IEnumerable<FlightServiceBatchChange> changes) =>
            new(new(rtrArrival), Services: changes.Where(change => change.Input is not null)
                .Select(change => new ReturnToRampServiceChange(change.RecordId, change.Input!)).ToArray(), Tasks: []);

        Task<HttpResponseMessage> WriteServicesAsync(HttpClient client, string version, params FlightServiceBatchChange[] changes) =>
            WritePortalOperationsAsync(client, id, HttpMethod.Put, route,
                returnToRamp ? RtrInput(changes) : OperationsBatchInput(services: changes), version);

        async Task<HttpResponseMessage> RemoveSecondOnMobileAsync(string version)
        {
            if (!returnToRamp)
                return await SendOperationsBatchAsync(editorClient, id, OperationsBatchInput(services: [new(second.RecordId, null)]), version);

            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/{route}")
            {
                Content = JsonContent.Create(RtrInput([]))
            };
            request.Headers.TryAddWithoutValidation("If-Match", version);
            return await editorClient.SendAsync(request);
        }
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

    private static void AssertReadOnlyPortalOperations(FlightDetailDto detail)
    {
        detail.CurrentStaffMemberId.ShouldBeNull();
        detail.CanSaveArrival.ShouldBeFalse();
        detail.CanSaveDeparture.ShouldBeFalse();
        detail.CanSaveCancellation.ShouldBeFalse();
        detail.CanSaveServices.ShouldBeFalse();
        detail.CanSaveTasks.ShouldBeFalse();
        detail.CanSaveReturnsToRamp.ShouldBeFalse();
        detail.CanAddServices.ShouldBe(false);
        detail.CanAddTasks.ShouldBe(false);
    }

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
