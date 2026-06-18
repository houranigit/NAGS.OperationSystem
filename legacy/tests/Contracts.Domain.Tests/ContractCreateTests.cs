using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Contracts.Domain.Enumerations;
using Contracts.Domain.Tests.Fixtures;
using Contracts.Domain.ValueObjects;
using Xunit;
using ContractAggregate = Contracts.Domain.Aggregates.Contract.Contract;

namespace Contracts.Domain.Tests;

public sealed class ContractCreateTests
{
    [Fact]
    public void Create_with_valid_inputs_succeeds_and_starts_in_draft()
    {
        var contract = ContractFixture.BuildValidContract(
            start: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            end: new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero),
            now: new DateTimeOffset(2029, 12, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(ContractStatus.Draft, contract.Status);
        Assert.Single(contract.Stations);
        Assert.Single(contract.OperationTypes);
        Assert.Equal("C-001", contract.ContractNo.Value);
    }

    [Fact]
    public void Create_now_inside_period_starts_active()
    {
        var contract = ContractFixture.BuildValidContract(
            start: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            end: new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero),
            now: new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void Create_with_AdHoc_operation_type_fails()
    {
        var adHocDraft = new ContractOperationTypeDraft(
            OperationTypeSnapshot.Create(ContractFixture.AdHocOperationTypeId, "AdHoc").Value!,
            new[] { ContractFixture.Service() });

        var result = TryCreate(operationTypes: new[] { adHocDraft });

        Assert.True(result.IsFailure);
        Assert.Contains("Ad Hoc", result.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_with_OT_having_no_services_fails()
    {
        var ot = new ContractOperationTypeDraft(
            ContractFixture.OperationType(ContractFixture.OpType1Id, "OT1"),
            Array.Empty<ServiceSnapshot>());

        var result = TryCreate(operationTypes: new[] { ot });

        Assert.True(result.IsFailure);
        Assert.Contains("at least 1 service", result.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_with_AOG_and_other_service_for_same_OT_fails()
    {
        var ot = new ContractOperationTypeDraft(
            ContractFixture.OperationType(ContractFixture.OpType1Id, "OT1"),
            new[]
            {
                ContractFixture.Service(ContractFixture.AogServiceId, "AOG", isAog: true),
                ContractFixture.Service(ContractFixture.Service2Id, "Other Service", isAog: false),
            });

        var result = TryCreate(operationTypes: new[] { ot });

        Assert.True(result.IsFailure);
        Assert.Contains("AOG", result.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_with_only_AOG_for_an_OT_succeeds_and_marks_AOG_only()
    {
        var ot = new ContractOperationTypeDraft(
            ContractFixture.OperationType(ContractFixture.OpType1Id, "OT1"),
            new[] { ContractFixture.Service(ContractFixture.AogServiceId, "AOG", isAog: true) });

        var contract = ContractFixture.BuildValidContract(operationTypes: new[] { ot });

        Assert.Single(contract.OperationTypes);
        Assert.True(contract.OperationTypes[0].IsAogOnly());
    }

    [Fact]
    public void Create_with_pricing_for_OT_not_in_contract_fails()
    {
        var ot = new ContractOperationTypeDraft(
            ContractFixture.OperationType(ContractFixture.OpType1Id, "OT1"),
            new[] { ContractFixture.Service(ContractFixture.Service1Id, "Daily Check") });

        // Pricing row references OT2, which is NOT in the contract's OT list.
        var pricing = new[]
        {
            ContractFixture.ServiceDraft(ContractFixture.OpType2Id, ContractFixture.Service2Id, "Other"),
        };

        var result = TryCreate(operationTypes: new[] { ot }, services: pricing);

        Assert.True(result.IsFailure);
        Assert.Contains("not part of the contract", result.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_with_no_pricing_rows_succeeds_and_uses_system_defaults()
    {
        // Service pricing rows are entirely optional now.
        var ot = new ContractOperationTypeDraft(
            ContractFixture.OperationType(ContractFixture.OpType1Id, "OT1"),
            new[] { ContractFixture.Service(ContractFixture.Service1Id, "Daily Check") });

        var contract = ContractFixture.BuildValidContract(
            operationTypes: new[] { ot },
            services: Array.Empty<ContractServiceDraft>());

        Assert.Empty(contract.Services);
    }

    [Fact]
    public void Create_with_DebriefRequired_persists_the_flag()
    {
        var contract = ContractFixture.BuildValidContract(debriefRequired: true);
        Assert.True(contract.DebriefRequired);
    }

    [Fact]
    public void Create_with_duplicate_stations_fails()
    {
        var dupStation = ContractFixture.Station();
        var contractNo = ContractNo.Create("C-001").Value;

        var result = ContractAggregate.Create(
            contractNo: contractNo,
            customer: ContractFixture.Customer(),
            currency: ContractFixture.Currency(),
            period: ContractFixture.Period(),
            paymentTerms: PaymentTerms.Net30,
            applyVat: false,
            debriefRequired: false,
            attachment: null,
            feesAndRates: ContractFixture.Fees(),
            advancePayments: Array.Empty<ContractAdvancePaymentDraft>(),
            cancellationPlan: ContractFixture.CancellationPerCancel(),
            delayPlan: ContractFixture.DelayPerDelay(),
            stations: new[] { dupStation, dupStation },
            operationTypes: new[] { ContractFixture.OperationTypeDraft() },
            services: Array.Empty<ContractServiceDraft>(),
            manpowers: Array.Empty<ContractManpowerDraft>(),
            tools: Array.Empty<ContractToolDraft>(),
            materials: Array.Empty<ContractMaterialDraft>(),
            generalSupports: Array.Empty<ContractGeneralSupportDraft>(),
            adHocOperationTypeId: ContractFixture.AdHocOperationTypeId,
            createdByUserId: ContractFixture.SystemUserId,
            now: new DateTimeOffset(2029, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.True(result.IsFailure);
        Assert.Contains("more than once", result.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static BuildingBlocks.Domain.Results.Result<ContractAggregate> TryCreate(
        IReadOnlyList<ContractOperationTypeDraft>? operationTypes = null,
        IReadOnlyList<ContractServiceDraft>? services = null)
    {
        operationTypes ??= new[] { ContractFixture.OperationTypeDraft() };
        services ??= Array.Empty<ContractServiceDraft>();

        return ContractAggregate.Create(
            contractNo: ContractNo.Create("C-001").Value,
            customer: ContractFixture.Customer(),
            currency: ContractFixture.Currency(),
            period: ContractFixture.Period(),
            paymentTerms: PaymentTerms.Net30,
            applyVat: false,
            debriefRequired: false,
            attachment: null,
            feesAndRates: ContractFixture.Fees(),
            advancePayments: Array.Empty<ContractAdvancePaymentDraft>(),
            cancellationPlan: ContractFixture.CancellationPerCancel(),
            delayPlan: ContractFixture.DelayPerDelay(),
            stations: new[] { ContractFixture.Station() },
            operationTypes: operationTypes,
            services: services,
            manpowers: Array.Empty<ContractManpowerDraft>(),
            tools: Array.Empty<ContractToolDraft>(),
            materials: Array.Empty<ContractMaterialDraft>(),
            generalSupports: Array.Empty<ContractGeneralSupportDraft>(),
            adHocOperationTypeId: ContractFixture.AdHocOperationTypeId,
            createdByUserId: ContractFixture.SystemUserId,
            now: new DateTimeOffset(2029, 6, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
