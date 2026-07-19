using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Features.Mobile;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Mobile;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class MobileMutationFingerprintCompatibilityTests
{
    private static readonly DateTimeOffset FromUtc =
        new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindReplay_AcceptsFingerprintWrittenBeforeIdentityAndProvenanceFields()
    {
        var ownerUserId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var flightId = Guid.NewGuid();
        var clientMutationId = Guid.NewGuid().ToString();
        var serviceLineId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var input = CurrentEnvelope(
            workOrderId,
            Payload(serviceLineId, taskId, serviceIsReturnToRamp: true, taskIsReturnToRamp: true));
        var preDeploymentFingerprint = Fingerprint(LegacyEnvelope(input));

        MobileMutations.PreReturnToRampFingerprint(input).ShouldBe(preDeploymentFingerprint);
        MobileMutations.Fingerprint(input).ShouldNotBe(preDeploymentFingerprint);

        await using var db = CreateDb();
        db.MobileMutations.Add(MobileMutation.Record(
            clientMutationId,
            ownerUserId,
            "update-work-order",
            workOrderId,
            flightId,
            clientFlightId: null,
            preDeploymentFingerprint,
            FromUtc));
        await db.SaveChangesAsync();

        var replay = await MobileMutations.FindReplayAsync(
            db,
            clientMutationId,
            ownerUserId,
            "update-work-order",
            MobileMutations.Fingerprint(input),
            expectedWorkOrderId: workOrderId,
            expectedFlightId: null,
            expectedClientFlightId: null,
            CancellationToken.None,
            MobileMutations.PreReturnToRampFingerprint(input));

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.ShouldNotBeNull();
        replay.Value!.WorkOrderId.ShouldBe(workOrderId);
    }

    [Fact]
    public async Task FindReplay_RejectsSemanticChangesAgainstCurrentSchemaFingerprint()
    {
        var ownerUserId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var flightId = Guid.NewGuid();
        var clientMutationId = Guid.NewGuid().ToString();
        var serviceLineId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var original = CurrentEnvelope(workOrderId, Payload(serviceLineId, taskId));
        var originalFingerprint = MobileMutations.Fingerprint(original);

        await using var db = CreateDb();
        db.MobileMutations.Add(MobileMutation.Record(
            clientMutationId,
            ownerUserId,
            "update-work-order",
            workOrderId,
            flightId,
            clientFlightId: null,
            originalFingerprint,
            FromUtc));
        await db.SaveChangesAsync();

        var changedInputs = new[]
        {
            CurrentEnvelope(workOrderId, Payload(serviceLineId, taskId, serviceIsReturnToRamp: true)),
            CurrentEnvelope(workOrderId, Payload(serviceLineId, taskId, taskIsReturnToRamp: true)),
            CurrentEnvelope(workOrderId, Payload(Guid.NewGuid(), taskId)),
            CurrentEnvelope(workOrderId, Payload(serviceLineId, taskId, description: "Changed")),
            original with { ServiceLineIdentityVersion = 2 }
        };

        foreach (var changed in changedInputs)
        {
            var replay = await MobileMutations.FindReplayAsync(
                db,
                clientMutationId,
                ownerUserId,
                "update-work-order",
                MobileMutations.Fingerprint(changed),
                expectedWorkOrderId: workOrderId,
                expectedFlightId: null,
                expectedClientFlightId: null,
                CancellationToken.None,
                MobileMutations.PreReturnToRampFingerprint(changed));

            replay.IsFailure.ShouldBeTrue();
            replay.Error.Code.ShouldBe("Operations.Mobile.MutationKeyReused");
        }
    }

    private static CurrentUpdateFingerprintEnvelope CurrentEnvelope(
        Guid workOrderId,
        WorkOrderEditableCommandPayload payload) =>
        new(workOrderId, WorkOrderType.Completion, payload, "AQID", ServiceLineIdentityVersion: 1);

    private static WorkOrderEditableCommandPayload Payload(
        Guid serviceLineId,
        Guid taskId,
        bool serviceIsReturnToRamp = false,
        bool taskIsReturnToRamp = false,
        string description = "Handled") =>
        new(
            ActualFlightNumber: "MOB100",
            AircraftTypeId: null,
            AircraftTailNumber: "HZ-TEST",
            ActualArrivalUtc: FromUtc,
            ActualDepartureUtc: FromUtc.AddHours(1),
            CanceledAtUtc: null,
            CancellationReason: null,
            Remarks: "Mobile update",
            ServiceLines:
            [
                new WorkOrderServiceLineCommand(
                    Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    FromUtc,
                    FromUtc.AddMinutes(30),
                    description,
                    serviceIsReturnToRamp,
                    serviceLineId)
            ],
            Tasks:
            [
                new WorkOrderTaskCommand(
                    taskId,
                    TaskType.Minor,
                    "Inspection",
                    FromUtc,
                    FromUtc.AddMinutes(20),
                    EmployeeIds: [],
                    Tools: [],
                    Materials: [],
                    GeneralSupports: [],
                    Attachments: null,
                    IsReturnToRamp: taskIsReturnToRamp)
            ]);

    private static LegacyUpdateFingerprintEnvelope LegacyEnvelope(CurrentUpdateFingerprintEnvelope current) =>
        new(
            current.WorkOrderId,
            current.Type,
            new LegacyWorkOrderPayload(
                current.Payload.ActualFlightNumber,
                current.Payload.AircraftTypeId,
                current.Payload.AircraftTailNumber,
                current.Payload.ActualArrivalUtc,
                current.Payload.ActualDepartureUtc,
                current.Payload.CanceledAtUtc,
                current.Payload.CancellationReason,
                current.Payload.Remarks,
                current.Payload.ServiceLines.Select(line => new LegacyServiceLine(
                    line.ServiceId,
                    line.PerformedByStaffMemberId,
                    line.FromUtc,
                    line.ToUtc,
                    line.Description)).ToList(),
                current.Payload.Tasks.Select(task => new LegacyTask(
                    task.Id,
                    task.TaskType,
                    task.Description,
                    task.FromUtc,
                    task.ToUtc,
                    task.EmployeeIds,
                    task.Tools,
                    task.Materials,
                    task.GeneralSupports,
                    task.Attachments)).ToList(),
                current.Payload.CustomerSignature),
            current.BaseRowVersion);

    private static string Fingerprint<T>(T request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static OperationsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OperationsDbContext(options);
    }

    private sealed record CurrentUpdateFingerprintEnvelope(
        Guid WorkOrderId,
        WorkOrderType Type,
        WorkOrderEditableCommandPayload Payload,
        string BaseRowVersion,
        int ServiceLineIdentityVersion);

    private sealed record LegacyUpdateFingerprintEnvelope(
        Guid WorkOrderId,
        WorkOrderType Type,
        LegacyWorkOrderPayload Payload,
        string BaseRowVersion);

    private sealed record LegacyWorkOrderPayload(
        string? ActualFlightNumber,
        Guid? AircraftTypeId,
        string? AircraftTailNumber,
        DateTimeOffset? ActualArrivalUtc,
        DateTimeOffset? ActualDepartureUtc,
        DateTimeOffset? CanceledAtUtc,
        string? CancellationReason,
        string? Remarks,
        IReadOnlyList<LegacyServiceLine> ServiceLines,
        IReadOnlyList<LegacyTask> Tasks,
        WorkOrderSignatureCommand? CustomerSignature = null);

    private sealed record LegacyServiceLine(
        Guid ServiceId,
        Guid PerformedByStaffMemberId,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        string? Description);

    private sealed record LegacyTask(
        Guid? Id,
        TaskType TaskType,
        string? Description,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        IReadOnlyList<Guid> EmployeeIds,
        IReadOnlyList<WorkOrderTaskToolCommand> Tools,
        IReadOnlyList<WorkOrderTaskMaterialCommand> Materials,
        IReadOnlyList<WorkOrderTaskGeneralSupportCommand> GeneralSupports,
        IReadOnlyList<WorkOrderTaskAttachmentCommand>? Attachments = null);
}
