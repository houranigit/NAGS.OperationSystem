using Operations.Application.Features.Mobile;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class MobileReturnToRampProvenanceTests
{
    [Fact]
    public void ProtectNew_ForcesAllRowsToNormalAndDropsServiceLineIdentities()
    {
        var payload = Payload(
            [ServiceLine(Guid.NewGuid(), isReturnToRamp: true)],
            [Task(Guid.NewGuid(), isReturnToRamp: true)]);

        var protectedPayload = MobileReturnToRampProvenance.ProtectNew(payload);

        protectedPayload.ServiceLines.ShouldHaveSingleItem().Id.ShouldBeNull();
        protectedPayload.ServiceLines[0].IsReturnToRamp.ShouldBeFalse();
        protectedPayload.Tasks.ShouldHaveSingleItem().Id.ShouldBeNull();
        protectedPayload.Tasks.ShouldHaveSingleItem().IsReturnToRamp.ShouldBeFalse();
    }

    [Fact]
    public void ProtectUpdate_DerivesExistingFlagsFromServerAndForcesNewRowsToNormal()
    {
        var normalLineId = Guid.NewGuid();
        var rtrLineId = Guid.NewGuid();
        var normalTaskId = Guid.NewGuid();
        var rtrTaskId = Guid.NewGuid();
        var payload = Payload(
            [
                ServiceLine(normalLineId, isReturnToRamp: true),
                ServiceLine(rtrLineId, isReturnToRamp: false),
                ServiceLine(null, isReturnToRamp: true)
            ],
            [
                Task(normalTaskId, isReturnToRamp: true),
                Task(rtrTaskId, isReturnToRamp: false),
                Task(null, isReturnToRamp: true)
            ]);

        var result = MobileReturnToRampProvenance.ProtectUpdate(
            payload,
            new Dictionary<Guid, bool> { [normalLineId] = false, [rtrLineId] = true },
            new Dictionary<Guid, bool> { [normalTaskId] = false, [rtrTaskId] = true },
            MobileReturnToRampProvenance.StableServiceLineIdentityVersion);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ServiceLines.Select(line => line.IsReturnToRamp).ShouldBe([false, true, false]);
        result.Value.Tasks.Select(task => task.IsReturnToRamp).ShouldBe([false, true, false]);
    }

    [Fact]
    public void ProtectUpdate_RejectsDuplicateOrForeignServiceLineIdentities()
    {
        var existingId = Guid.NewGuid();
        var duplicate = MobileReturnToRampProvenance.ProtectUpdate(
            Payload([ServiceLine(existingId), ServiceLine(existingId)], []),
            new Dictionary<Guid, bool> { [existingId] = true },
            new Dictionary<Guid, bool>(),
            MobileReturnToRampProvenance.StableServiceLineIdentityVersion);

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Code.ShouldBe("Operations.Mobile.ServiceLineIdsDuplicate");

        var foreign = MobileReturnToRampProvenance.ProtectUpdate(
            Payload([ServiceLine(Guid.NewGuid())], []),
            new Dictionary<Guid, bool> { [existingId] = true },
            new Dictionary<Guid, bool>(),
            MobileReturnToRampProvenance.StableServiceLineIdentityVersion);

        foreign.IsFailure.ShouldBeTrue();
        foreign.Error.Code.ShouldBe("Operations.Mobile.ServiceLineIdForeign");
    }

    [Fact]
    public void ProtectUpdate_RejectsLegacyFullReplacementThatCouldErasePersistedRtrProvenance()
    {
        var result = MobileReturnToRampProvenance.ProtectUpdate(
            Payload([ServiceLine(null)], []),
            new Dictionary<Guid, bool> { [Guid.NewGuid()] = true },
            new Dictionary<Guid, bool>(),
            serviceLineIdentityVersion: 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Mobile.ServiceLineIdentityRequired");
    }

    private static WorkOrderEditableCommandPayload Payload(
        IReadOnlyList<WorkOrderServiceLineCommand> serviceLines,
        IReadOnlyList<WorkOrderTaskCommand> tasks) =>
        new(
            ActualFlightNumber: "RJ100",
            AircraftTypeId: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: null,
            ActualDepartureUtc: null,
            CanceledAtUtc: null,
            CancellationReason: null,
            Remarks: null,
            serviceLines,
            tasks);

    private static WorkOrderServiceLineCommand ServiceLine(Guid? id, bool isReturnToRamp = false) =>
        new(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(10),
            Description: null,
            IsReturnToRamp: isReturnToRamp,
            Id: id);

    private static WorkOrderTaskCommand Task(Guid? id, bool isReturnToRamp = false) =>
        new(
            id,
            TaskType.Minor,
            Description: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(10),
            EmployeeIds: [],
            Tools: [],
            Materials: [],
            GeneralSupports: [],
            Attachments: null,
            IsReturnToRamp: isReturnToRamp);
}
