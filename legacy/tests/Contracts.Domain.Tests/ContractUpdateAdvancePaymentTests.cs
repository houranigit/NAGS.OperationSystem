using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Contracts.Domain.Enumerations;
using Contracts.Domain.Tests.Fixtures;
using Contracts.Domain.ValueObjects;
using Xunit;
using ContractAggregate = Contracts.Domain.Aggregates.Contract.Contract;

namespace Contracts.Domain.Tests;

/// <summary>
/// Focused tests for per-OT <see cref="ContractAdvancePayment"/> behavior across
/// <see cref="ContractAggregate.Update"/>. The Update path clears and rebuilds child
/// collections — these tests pin the contract that:
/// <list type="number">
/// <item>An advance-payment row's entity id is preserved when the wizard re-submits its
/// existing id, so EF tracks the row as an UPDATE (and downstream FKs survive).</item>
/// <item>A row with no existing id is treated as a brand-new add and gets a fresh id.</item>
/// <item>Two rows for the same OT are rejected even when one carries an existing id.</item>
/// </list>
/// </summary>
public sealed class ContractUpdateAdvancePaymentTests
{
    [Fact]
    public void Update_preserves_existing_advance_payment_row_id_when_supplied()
    {
        var ot = ContractFixture.OperationType(ContractFixture.OpType1Id, "OT1");
        var contract = BuildContractWithAdvancePayment(
            new[] { ot },
            new ContractAdvancePaymentDraft(
                OperationTypeId: ot.OperationTypeId,
                FlightsCount: 10,
                FlightCost: 200m,
                Balance: 1000m,
                Deposit: 500m,
                ExistingContractAdvancePaymentId: null));

        var originalId = contract.AdvancePayments[0].Id.Value;

        var updateResult = TryUpdate(contract,
            new[] { ot },
            new ContractAdvancePaymentDraft(
                OperationTypeId: ot.OperationTypeId,
                FlightsCount: 12, // bumped
                FlightCost: 250m,
                Balance: 1500m,
                Deposit: 600m,
                ExistingContractAdvancePaymentId: originalId));

        Assert.True(updateResult.IsSuccess, updateResult.IsFailure ? updateResult.Error.Description : null);
        Assert.Single(contract.AdvancePayments);
        Assert.Equal(originalId, contract.AdvancePayments[0].Id.Value);
        Assert.Equal(12, contract.AdvancePayments[0].Payment.FlightsCount);
    }

    [Fact]
    public void Update_assigns_fresh_id_when_no_existing_id_supplied()
    {
        var ot = ContractFixture.OperationType(ContractFixture.OpType1Id, "OT1");
        var contract = BuildContractWithAdvancePayment(
            new[] { ot },
            new ContractAdvancePaymentDraft(
                OperationTypeId: ot.OperationTypeId,
                FlightsCount: 10,
                FlightCost: 200m,
                Balance: 1000m,
                Deposit: 500m,
                ExistingContractAdvancePaymentId: null));

        var originalId = contract.AdvancePayments[0].Id.Value;

        // Caller "forgot" to round-trip the existing id — the aggregate must still build
        // a row but with a fresh id; downstream EF tracks this as DELETE+INSERT.
        var updateResult = TryUpdate(contract,
            new[] { ot },
            new ContractAdvancePaymentDraft(
                OperationTypeId: ot.OperationTypeId,
                FlightsCount: 10,
                FlightCost: 200m,
                Balance: 1000m,
                Deposit: 500m,
                ExistingContractAdvancePaymentId: null));

        Assert.True(updateResult.IsSuccess);
        Assert.Single(contract.AdvancePayments);
        Assert.NotEqual(originalId, contract.AdvancePayments[0].Id.Value);
    }

    [Fact]
    public void Update_with_two_advance_payment_rows_for_same_operation_type_fails()
    {
        var ot = ContractFixture.OperationType(ContractFixture.OpType1Id, "OT1");
        var contract = BuildContractWithAdvancePayment(
            new[] { ot },
            new ContractAdvancePaymentDraft(
                OperationTypeId: ot.OperationTypeId,
                FlightsCount: 10,
                FlightCost: 200m,
                Balance: 1000m,
                Deposit: 500m,
                ExistingContractAdvancePaymentId: null));

        var originalId = contract.AdvancePayments[0].Id.Value;

        var updateResult = TryUpdate(contract,
            new[] { ot },
            new ContractAdvancePaymentDraft(
                OperationTypeId: ot.OperationTypeId,
                FlightsCount: 5,
                FlightCost: 100m,
                Balance: 500m,
                Deposit: 200m,
                ExistingContractAdvancePaymentId: originalId),
            new ContractAdvancePaymentDraft(
                OperationTypeId: ot.OperationTypeId,
                FlightsCount: 8,
                FlightCost: 150m,
                Balance: 700m,
                Deposit: 300m,
                ExistingContractAdvancePaymentId: null));

        Assert.True(updateResult.IsFailure);
        Assert.Contains("only one is allowed", updateResult.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static ContractAggregate BuildContractWithAdvancePayment(
        IReadOnlyList<OperationTypeSnapshot> operationTypes,
        params ContractAdvancePaymentDraft[] advancePayments)
    {
        var otDrafts = operationTypes
            .Select(ot => new ContractOperationTypeDraft(ot, new[] { ContractFixture.Service(ContractFixture.Service1Id, "Daily Check") }))
            .ToArray();

        var result = ContractAggregate.Create(
            contractNo: ContractNo.Create("C-001").Value,
            customer: ContractFixture.Customer(),
            currency: ContractFixture.Currency(),
            period: ContractFixture.Period(),
            paymentTerms: PaymentTerms.Net30,
            applyVat: false,
            debriefRequired: false,
            attachment: null,
            feesAndRates: ContractFixture.Fees(),
            advancePayments: advancePayments,
            cancellationPlan: ContractFixture.CancellationPerCancel(),
            delayPlan: ContractFixture.DelayPerDelay(),
            stations: new[] { ContractFixture.Station() },
            operationTypes: otDrafts,
            services: Array.Empty<ContractServiceDraft>(),
            manpowers: Array.Empty<ContractManpowerDraft>(),
            tools: Array.Empty<ContractToolDraft>(),
            materials: Array.Empty<ContractMaterialDraft>(),
            generalSupports: Array.Empty<ContractGeneralSupportDraft>(),
            adHocOperationTypeId: ContractFixture.AdHocOperationTypeId,
            createdByUserId: ContractFixture.SystemUserId,
            now: new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero));

        if (result.IsFailure)
            throw new InvalidOperationException($"Fixture should build a valid contract, got: {result.Error.Description}");
        return result.Value;
    }

    private static BuildingBlocks.Domain.Results.Result TryUpdate(
        ContractAggregate contract,
        IReadOnlyList<OperationTypeSnapshot> operationTypes,
        params ContractAdvancePaymentDraft[] advancePayments)
    {
        var otDrafts = operationTypes
            .Select(ot => new ContractOperationTypeDraft(ot, new[] { ContractFixture.Service(ContractFixture.Service1Id, "Daily Check") }))
            .ToArray();

        return contract.Update(
            contractNo: ContractNo.Create("C-001").Value,
            customer: ContractFixture.Customer(),
            currency: ContractFixture.Currency(),
            period: ContractFixture.Period(),
            paymentTerms: PaymentTerms.Net30,
            applyVat: false,
            debriefRequired: false,
            attachment: null,
            feesAndRates: ContractFixture.Fees(),
            advancePayments: advancePayments,
            cancellationPlan: ContractFixture.CancellationPerCancel(),
            delayPlan: ContractFixture.DelayPerDelay(),
            stations: new[] { ContractFixture.Station() },
            operationTypes: otDrafts,
            services: Array.Empty<ContractServiceDraft>(),
            manpowers: Array.Empty<ContractManpowerDraft>(),
            tools: Array.Empty<ContractToolDraft>(),
            materials: Array.Empty<ContractMaterialDraft>(),
            generalSupports: Array.Empty<ContractGeneralSupportDraft>(),
            adHocOperationTypeId: ContractFixture.AdHocOperationTypeId,
            updatedByUserId: ContractFixture.SystemUserId,
            now: new DateTimeOffset(2030, 6, 2, 0, 0, 0, TimeSpan.Zero));
    }
}
