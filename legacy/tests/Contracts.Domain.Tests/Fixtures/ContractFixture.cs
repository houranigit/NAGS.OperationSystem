using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Contracts.Domain.Enumerations;
using Contracts.Domain.ValueObjects;
using ContractAggregate = Contracts.Domain.Aggregates.Contract.Contract;

namespace Contracts.Domain.Tests.Fixtures;

/// <summary>
/// Builders that produce fully-validated <see cref="Contract"/> aggregates without polluting
/// the test bodies with snapshot/value-object boilerplate. Each helper exposes Guid seeds so
/// tests can override individual rows or build conflict scenarios deterministically.
/// </summary>
public static class ContractFixture
{
    public static readonly Guid AdHocOperationTypeId = new("30000000-0000-0000-0000-000000000001");
    public static readonly Guid AogServiceId = new("40000000-0000-0000-0000-000000000001");
    public static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000777");

    public static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid CurrencyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Station1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid OpType1Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid OpType2Id = Guid.Parse("44444444-4444-4444-4444-444444444445");
    public static readonly Guid Service1Id = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Service2Id = Guid.Parse("55555555-5555-5555-5555-555555555556");

    public static CustomerSnapshot Customer(Guid? id = null) =>
        CustomerSnapshot.Create(id ?? CustomerId, "RJ", "Royal Jordanian").Value;

    public static CurrencySnapshot Currency(Guid? id = null) =>
        CurrencySnapshot.Create(id ?? CurrencyId, "USD").Value;

    public static StationSnapshot Station(Guid? id = null, string iata = "JED") =>
        StationSnapshot.Create(id ?? Station1Id, iata, $"{iata} Station").Value;

    public static OperationTypeSnapshot OperationType(Guid? id = null, string name = "Scheduled") =>
        OperationTypeSnapshot.Create(id ?? OpType1Id, name).Value;

    public static ServiceSnapshot Service(Guid? id = null, string name = "Daily Check", bool isAog = false) =>
        ServiceSnapshot.Create(id ?? Service1Id, name, isAog).Value;

    public static ContractPeriod Period(
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        int alertDays = 0,
        ExpiryAlertInterval? alertInterval = null)
    {
        var s = start ?? new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var e = end ?? s.AddYears(1);
        return ContractPeriod.Create(s, e, alertDays, alertInterval).Value;
    }

    public static FeesAndRates Fees() => FeesAndRates.Create(
        adminFee: Fee.Create(FeeType.Fixed, 10m).Value,
        disbursementFee: Fee.Create(FeeType.Percentage, 5m).Value,
        holidayFee: Fee.FixedZero,
        nightFee: Fee.FixedZero,
        returnToRampDiscount: Fee.FixedZero,
        otherDiscount: Fee.FixedZero).Value;

    public static CancellationChargePlan CancellationPerCancel(decimal flatValue = 100m) =>
        CancellationChargePlan.Create(
            CancellationChargeBasis.PerCancel,
            FeeType.Fixed,
            new[] { new CancellationBracketRow(0, 0, flatValue) }).Value;

    public static DelayChargePlan DelayPerDelay(decimal flatValue = 50m) =>
        DelayChargePlan.Create(
            DelayType.LateDeparture,
            DelayChargeBasis.PerDelay,
            FeeType.Fixed,
            new[] { new DelayBracketRow(0, 0, flatValue) }).Value;

    /// <summary>Builds a single flat-priced service line for the given operation type.</summary>
    public static ContractServiceDraft ServiceDraft(
        Guid operationTypeId,
        Guid serviceId,
        string serviceName = "Daily Check",
        bool isAog = false,
        decimal flatPrice = 100m,
        Money? package = null) =>
        new(
            OperationType: OperationType(operationTypeId, $"OT-{operationTypeId:N}"[..6]),
            Service: Service(serviceId, serviceName, isAog),
            AircraftType: null,
            Basis: PricingBasis.Flat,
            PackagePaidBalance: package,
            Brackets: new[]
            {
                new ContractPriceBracket(
                    MinMinutes: 0,
                    MaxMinutes: null,
                    BlockSize: 1,
                    PriceValue: flatPrice,
                    PackagePriceValue: package is { IsPositive: true } ? flatPrice - 10m : null,
                    BillingMode: BracketBillingMode.ProRated)
            },
            ExistingContractServiceId: null);

    /// <summary>
    /// Convenience: builds an OT draft with one default service (non-AOG by default).
    /// Tests can pass an explicit service list to exercise the AOG-only / no-mix rules.
    /// </summary>
    public static ContractOperationTypeDraft OperationTypeDraft(
        Guid? operationTypeId = null,
        IReadOnlyList<ServiceSnapshot>? services = null)
    {
        var ot = OperationType(operationTypeId);
        var defaultServices = services ?? new[] { Service(Service1Id, "Daily Check", isAog: false) };
        return new ContractOperationTypeDraft(ot, defaultServices);
    }

    public static ContractAggregate BuildValidContract(
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        DateTimeOffset? now = null,
        IReadOnlyList<ContractOperationTypeDraft>? operationTypes = null,
        IReadOnlyList<ContractServiceDraft>? services = null,
        bool debriefRequired = false)
    {
        operationTypes ??= new[] { OperationTypeDraft() };
        services ??= Array.Empty<ContractServiceDraft>();

        var contractNo = ContractNo.Create("C-001").Value;
        var period = Period(start, end);
        var nowTs = now ?? new DateTimeOffset(2029, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var result = ContractAggregate.Create(
            contractNo: contractNo,
            customer: Customer(),
            currency: Currency(),
            period: period,
            paymentTerms: PaymentTerms.Net30,
            applyVat: true,
            debriefRequired: debriefRequired,
            attachment: null,
            feesAndRates: Fees(),
            advancePayments: Array.Empty<ContractAdvancePaymentDraft>(),
            cancellationPlan: CancellationPerCancel(),
            delayPlan: DelayPerDelay(),
            stations: new[] { Station() },
            operationTypes: operationTypes,
            services: services,
            manpowers: Array.Empty<ContractManpowerDraft>(),
            tools: Array.Empty<ContractToolDraft>(),
            materials: Array.Empty<ContractMaterialDraft>(),
            generalSupports: Array.Empty<ContractGeneralSupportDraft>(),
            adHocOperationTypeId: AdHocOperationTypeId,
            createdByUserId: SystemUserId,
            now: nowTs);

        if (result.IsFailure)
            throw new InvalidOperationException($"Fixture should build a valid contract, got error: {result.Error.Description}");
        return result.Value;
    }
}
