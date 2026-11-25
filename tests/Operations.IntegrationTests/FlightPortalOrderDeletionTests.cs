using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Operations.Application.Contracts;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Theory]
    [InlineData("8901", "work-order", "cancellation-order")]
    [InlineData("8902", "cancellation-order", "work-order")]
    public async Task Portal_order_deletion_resolves_conflict_and_preserves_the_other_order(
        string number, string deleteOrder, string keepOrder)
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var worker = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        var canceler = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        var secondCanceler = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var workerClient = worker.Client;
        using var cancelerClient = canceler.Client;
        using var secondCancelerClient = secondCanceler.Client;
        var ata = DateTimeOffset.UtcNow.AddHours(-1);
        var id = await SchedulePortalFlightAtAsync(admin, refs, number, ata, [worker.StaffId, canceler.StaffId, secondCanceler.StaffId]);
        var aircraft = await OperationsBatchAircraftAsync(admin);
        var initial = await PortalOperationsDetailAsync(admin, id);
        using var workResponse = await WritePortalOperationsAsync(workerClient, id, HttpMethod.Put, "batch",
            OperationsBatchInput(new(ata, aircraft, "HZ-DELETE", null, null), new(ata.AddMinutes(40), null, null, null)),
            initial.RowVersion);
        workResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var work = (await workResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var cancellation = new SaveFlightCancellationRequest(ata.AddMinutes(15), "First report", AcknowledgeConflictingSubmission: true);
        using var firstResponse = await WritePortalOperationsAsync(cancelerClient, id, HttpMethod.Put, "cancellation", cancellation, work.RowVersion);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = (await firstResponse.Content.ReadFromJsonAsync<FlightCancellationSavedDto>())!;
        using var secondResponse = await WritePortalOperationsAsync(secondCancelerClient, id, HttpMethod.Put, "cancellation",
            cancellation with { Remarks = "Second report" }, first.RowVersion);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var conflict = await PortalOperationsDetailAsync(admin, id);
        conflict.CancellationSubmissions!.Count.ShouldBe(2);
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(admin, id, HttpMethod.Put, "approval", null, conflict.RowVersion),
            "Operations.FlightApproval.ConflictingReports");

        var reportOwner = deleteOrder == "work-order" ? workerClient : cancelerClient;
        using var unauthorized = await WritePortalOperationsAsync(reportOwner, id, HttpMethod.Delete, deleteOrder, null, conflict.RowVersion);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var missingVersion = await admin.DeleteAsync($"{Base}/flights/{id}/operations/{deleteOrder}");
        missingVersion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missingVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("General.PreconditionRequired");
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(admin, id, HttpMethod.Delete, deleteOrder, null, first.RowVersion),
            "General.ConcurrencyConflict");
        (await PortalOperationsDetailAsync(admin, id)).RowVersion.ShouldBe(conflict.RowVersion);

        using var deleted = await WritePortalOperationsAsync(admin, id, HttpMethod.Delete, deleteOrder, null, conflict.RowVersion);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var resolved = await PortalOperationsDetailAsync(admin, id);
        resolved.RowVersion.ShouldNotBe(conflict.RowVersion);
        resolved.ScheduledArrivalUtc.ShouldBe(initial.ScheduledArrivalUtc);
        resolved.ScheduledDepartureUtc.ShouldBe(initial.ScheduledDepartureUtc);
        resolved.PlannedServices.ShouldBe(initial.PlannedServices);
        resolved.AssignedEmployees.ShouldBe(initial.AssignedEmployees);
        if (keepOrder == "work-order")
        {
            resolved.CancellationSubmissions!.ShouldBeEmpty();
            resolved.ArrivalSubmissions.ShouldBe(conflict.ArrivalSubmissions);
            resolved.DepartureSubmissions.ShouldBe(conflict.DepartureSubmissions);
        }
        else
        {
            resolved.ArrivalSubmissions!.ShouldBeEmpty();
            resolved.DepartureSubmissions!.ShouldBeEmpty();
            resolved.CancellationSubmissions.ShouldBe(conflict.CancellationSubmissions);
        }

        var approved = await ApprovePortalFlightAsync(admin, id, resolved.RowVersion);
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(admin, id, HttpMethod.Delete, keepOrder, null, approved.RowVersion),
            "Operations.FlightApproval.Locked");
        (await PortalOperationsDetailAsync(admin, id)).RowVersion.ShouldBe(approved.RowVersion);
    }

    [Fact]
    public async Task Portal_order_deletion_removes_normal_and_return_to_ramp_work_and_private_files()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var worker = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var workerClient = worker.Client;
        var ata = DateTimeOffset.UtcNow.AddHours(-2);
        var id = await SchedulePortalFlightAtAsync(admin, refs, "8903", ata, [worker.StaffId]);
        var aircraft = await OperationsBatchAircraftAsync(admin);
        var initial = await OperationsBatchSnapshotAsync(workerClient, id);
        var png = ServicePng();
        var attachment = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Image, "work.png", "image/png", png);
        var flightService = OperationsBatchClosedService(refs.ServiceId, worker.StaffId, ata.AddMinutes(10), ata.AddMinutes(30))
            with { Attachments = [attachment] };
        var flightTask = OperationsBatchClosedTask(worker.StaffId, ata.AddMinutes(30), ata.AddMinutes(50));
        var atd = ata.AddHours(1);
        using var batchResponse = await WritePortalOperationsAsync(workerClient, id, HttpMethod.Put, "batch",
            OperationsBatchInput(new(ata, aircraft, "HZ-DELETE", null, null), new(atd, png, atd, null),
                [new(Guid.NewGuid(), flightService)], [new(Guid.NewGuid(), flightTask)]), initial.Detail.RowVersion);
        batchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await batchResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;

        var returnedAt = atd.AddMinutes(15);
        var leftAgain = returnedAt.AddMinutes(30);
        var returnService = OperationsBatchClosedService(refs.ServiceId, worker.StaffId, returnedAt.AddMinutes(1), returnedAt.AddMinutes(20));
        var returnTask = OperationsBatchClosedTask(worker.StaffId, returnedAt.AddMinutes(5), returnedAt.AddMinutes(25))
            with { Attachments = [attachment with { Id = Guid.NewGuid(), FileName = "rtr.png" }] };
        var returnInput = new SaveFlightReturnToRampRequest(new(returnedAt), new(leftAgain, null, png, "signature.png", "image/png", leftAgain),
            [new(Guid.NewGuid(), returnService)], [new(Guid.NewGuid(), returnTask)]);
        using var returnResponse = await WritePortalOperationsAsync(workerClient, id, HttpMethod.Put, $"returns-to-ramp/{Guid.NewGuid()}",
            returnInput, saved.RowVersion);
        returnResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await OperationsBatchSnapshotAsync(workerClient, id);
        before.StoredFiles.Except(initial.StoredFiles).Count().ShouldBe(4);

        using var deleted = await WritePortalOperationsAsync(admin, id, HttpMethod.Delete, "work-order", null, before.Detail.RowVersion);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await OperationsBatchSnapshotAsync(workerClient, id);
        after.Detail.ArrivalSubmissions!.ShouldBeEmpty();
        after.Detail.DepartureSubmissions!.ShouldBeEmpty();
        after.Detail.Services!.ShouldBeEmpty();
        after.Detail.Tasks!.ShouldBeEmpty();
        after.Detail.ReturnToRamps!.ShouldBeEmpty();
        after.Detail.LastReturnToRampSequence.ShouldBe(before.Detail.LastReturnToRampSequence);
        after.StoredFiles.ShouldBe(initial.StoredFiles);
    }
}
