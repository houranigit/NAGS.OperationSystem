using MasterData.Contracts.Seeding;
using Operations.Application.Features.Mobile;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class MobileCreateScratchWorkOrderCommandValidatorTests
{
    public static IEnumerable<object?[]> UnknownCustomerIds()
    {
        yield return [null];
        yield return [(Guid?)Guid.Empty];
        yield return [(Guid?)WellKnownMasterDataIds.UnknownCustomer];
    }

    [Theory]
    [MemberData(nameof(UnknownCustomerIds))]
    public void Requires_remarks_when_customer_is_missing_empty_or_unknown(Guid? customerId)
    {
        var result = new MobileCreateScratchWorkOrderCommandValidator()
            .Validate(Command(customerId, remarks: "  "));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.PropertyName == "Payload.Remarks" &&
            error.ErrorMessage == "Remarks are required when the customer is unknown.");
    }

    [Fact]
    public void Allows_missing_remarks_when_a_known_customer_is_selected()
    {
        var result = new MobileCreateScratchWorkOrderCommandValidator()
            .Validate(Command(Guid.NewGuid(), remarks: null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Allows_unknown_customer_when_remarks_are_provided()
    {
        var result = new MobileCreateScratchWorkOrderCommandValidator()
            .Validate(Command(WellKnownMasterDataIds.UnknownCustomer, remarks: "Walk-in operator in blue uniform."));

        result.IsValid.ShouldBeTrue();
    }

    private static MobileCreateScratchWorkOrderCommand Command(Guid? customerId, string? remarks) =>
        new(
            customerId,
            "MOB100",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            AircraftTypeId: null,
            PlannedServiceIds: [],
            WorkOrderType.Completion,
            new WorkOrderEditableCommandPayload(
                ActualFlightNumber: null,
                AircraftTypeId: null,
                AircraftTailNumber: null,
                ActualArrivalUtc: null,
                ActualDepartureUtc: null,
                CanceledAtUtc: null,
                CancellationReason: null,
                Remarks: remarks,
                ServiceLines: [],
                Tasks: []),
            Guid.NewGuid().ToString(),
            Guid.NewGuid());
}
