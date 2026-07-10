using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderMergeCommandTests
{
    [Fact]
    public void MergeWorkOrdersCommandValidator_RequiresAtLeastTwoUniqueSources()
    {
        var validator = new MergeWorkOrdersCommandValidator();
        var sourceId = Guid.NewGuid();

        var oneSource = validator.Validate(Command([sourceId]));
        oneSource.IsValid.ShouldBeFalse();
        oneSource.Errors.ShouldContain(error => error.ErrorMessage == "At least two source work orders are required.");

        var duplicateSources = validator.Validate(Command([sourceId, sourceId]));
        duplicateSources.IsValid.ShouldBeFalse();
        duplicateSources.Errors.ShouldContain(error => error.ErrorMessage == "Source work orders must be unique.");

        var valid = validator.Validate(Command([sourceId, Guid.NewGuid()]));
        valid.IsValid.ShouldBeTrue();
    }

    private static MergeWorkOrdersCommand Command(IReadOnlyList<Guid> sourceIds) =>
        new(
            Guid.NewGuid(),
            sourceIds,
            WorkOrderType.Completion,
            new WorkOrderEditableCommandPayload(
                ActualFlightNumber: null,
                AircraftTypeId: null,
                AircraftTailNumber: null,
                ActualArrivalUtc: null,
                ActualDepartureUtc: null,
                CanceledAtUtc: null,
                CancellationReason: null,
                Remarks: null,
                ServiceLines: [],
                Tasks: []),
            ApproveImmediately: false);
}
