using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Contracts.Domain.Enumerations;
using Contracts.Domain.Events;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Customer ↔ Stations ↔ OperationTypes binding aggregate root. Owns pricing for services,
/// manpowers, tools, materials, and general-supports, plus charge plans for cancellation
/// and delay, fees, advance payment state, and a status lifecycle of
/// <c>Draft → Active → (Suspended) → Active → Expired</c> with an optional manual
/// <c>Terminated</c> exit.
/// </summary>
public sealed class Contract : AggregateRoot<ContractId>
{
    // -- Identity & Header --
    public ContractNo ContractNo { get; private set; } = null!;
    public CustomerSnapshot Customer { get; private set; } = null!;
    public Guid CustomerId { get; private set; }
    public CurrencySnapshot Currency { get; private set; } = null!;
    public Guid CurrencyId { get; private set; }
    public ContractPeriod Period { get; private set; } = null!;
    public PaymentTerms PaymentTerms { get; private set; }
    public bool ApplyVat { get; private set; }

    /// <summary>
    /// When true, work orders billed under this contract require a debrief step before
    /// being approved. Surfaced to the operations module via <c>IContractReadService</c>.
    /// </summary>
    public bool DebriefRequired { get; private set; }
    public byte[]? Attachment { get; private set; }

    // -- Owned VOs --
    public FeesAndRates FeesAndRates { get; private set; } = null!;

    // -- Charge plans (header data + bracket child entities) --
    public CancellationChargeBasis CancellationBasis { get; private set; }
    public FeeType CancellationChargeType { get; private set; }
    public DelayChargeBasis DelayBasis { get; private set; }
    public FeeType DelayChargeType { get; private set; }
    public DelayType DelayType { get; private set; }

    private readonly List<CancellationBracket> _cancellationBrackets = [];
    public IReadOnlyList<CancellationBracket> CancellationBrackets => _cancellationBrackets;

    private readonly List<DelayBracket> _delayBrackets = [];
    public IReadOnlyList<DelayBracket> DelayBrackets => _delayBrackets;

    // -- Child collections --
    private readonly List<ContractStation> _stations = [];
    public IReadOnlyList<ContractStation> Stations => _stations;

    private readonly List<ContractOperationType> _operationTypes = [];
    public IReadOnlyList<ContractOperationType> OperationTypes => _operationTypes;

    private readonly List<ContractService> _services = [];
    public IReadOnlyList<ContractService> Services => _services;

    private readonly List<ContractManpower> _manpowers = [];
    public IReadOnlyList<ContractManpower> Manpowers => _manpowers;

    private readonly List<ContractTool> _tools = [];
    public IReadOnlyList<ContractTool> Tools => _tools;

    private readonly List<ContractMaterial> _materials = [];
    public IReadOnlyList<ContractMaterial> Materials => _materials;

    private readonly List<ContractGeneralSupport> _generalSupports = [];
    public IReadOnlyList<ContractGeneralSupport> GeneralSupports => _generalSupports;

    /// <summary>
    /// Per-operation-type advance payment rows. Replaces the historical single
    /// <c>AdvancePayment</c> VO so a contract covering several OTs can pre-pay each one
    /// independently. Empty when the contract has no advance payments at all.
    /// </summary>
    private readonly List<ContractAdvancePayment> _advancePayments = [];
    public IReadOnlyList<ContractAdvancePayment> AdvancePayments => _advancePayments;

    // -- Lifecycle --
    public ContractStatus Status { get; private set; }
    public Termination? Termination { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    /// <summary>UTC instant of the last "expiring soon" notification raised for this contract.</summary>
    public DateTime? LastExpiringSoonNotificationAt { get; private set; }

    private Contract() { }

    /// <summary>
    /// Creates a fully-populated contract atomically. Caller passes the well-known
    /// <paramref name="adHocOperationTypeId"/> seed so the Domain stays independent of
    /// <c>Core.Contracts.Seeding</c>. The AOG-vs-others rule is enforced per-OT inside
    /// <see cref="ContractOperationType.Create"/>.
    /// </summary>
    public static Result<Contract> Create(
        ContractNo contractNo,
        CustomerSnapshot customer,
        CurrencySnapshot currency,
        ContractPeriod period,
        PaymentTerms paymentTerms,
        bool applyVat,
        bool debriefRequired,
        byte[]? attachment,
        FeesAndRates feesAndRates,
        IReadOnlyList<ContractAdvancePaymentDraft> advancePayments,
        CancellationChargePlan cancellationPlan,
        DelayChargePlan delayPlan,
        IReadOnlyList<StationSnapshot> stations,
        IReadOnlyList<ContractOperationTypeDraft> operationTypes,
        IReadOnlyList<ContractServiceDraft> services,
        IReadOnlyList<ContractManpowerDraft> manpowers,
        IReadOnlyList<ContractToolDraft> tools,
        IReadOnlyList<ContractMaterialDraft> materials,
        IReadOnlyList<ContractGeneralSupportDraft> generalSupports,
        Guid adHocOperationTypeId,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        var headerCheck = ValidateHeader(
            contractNo, customer, currency, period, paymentTerms,
            feesAndRates, cancellationPlan, delayPlan, createdByUserId);
        if (headerCheck.IsFailure) return headerCheck.Error;

        var stationsCheck = ValidateStations(stations);
        if (stationsCheck.IsFailure) return stationsCheck.Error;

        var operationTypesCheck = ValidateOperationTypes(operationTypes, adHocOperationTypeId);
        if (operationTypesCheck.IsFailure) return operationTypesCheck.Error;

        var operationTypeIds = operationTypes.Select(o => o.OperationType.OperationTypeId).ToHashSet();

        var servicesCheck = ValidateServices(services, operationTypeIds);
        if (servicesCheck.IsFailure) return servicesCheck.Error;

        var manpowersCheck = ValidateManpowers(manpowers, operationTypeIds);
        if (manpowersCheck.IsFailure) return manpowersCheck.Error;

        var toolsCheck = ValidateTools(tools, operationTypeIds);
        if (toolsCheck.IsFailure) return toolsCheck.Error;

        var materialsCheck = ValidateMaterials(materials, operationTypeIds);
        if (materialsCheck.IsFailure) return materialsCheck.Error;

        var generalSupportsCheck = ValidateGeneralSupports(generalSupports, operationTypeIds);
        if (generalSupportsCheck.IsFailure) return generalSupportsCheck.Error;

        var advancePaymentsCheck = ValidateAdvancePayments(advancePayments, operationTypeIds, adHocOperationTypeId);
        if (advancePaymentsCheck.IsFailure) return advancePaymentsCheck.Error;

        var contractId = ContractId.New();

        var contract = new Contract
        {
            Id = contractId,
            ContractNo = contractNo,
            Customer = customer,
            CustomerId = customer.CustomerId,
            Currency = currency,
            CurrencyId = currency.CurrencyId,
            Period = period,
            PaymentTerms = paymentTerms,
            ApplyVat = applyVat,
            DebriefRequired = debriefRequired,
            Attachment = attachment,
            FeesAndRates = feesAndRates,
            CancellationBasis = cancellationPlan.Basis,
            CancellationChargeType = cancellationPlan.ChargeType,
            DelayBasis = delayPlan.Basis,
            DelayChargeType = delayPlan.ChargeType,
            DelayType = delayPlan.DelayType,
            CreatedAt = now.UtcDateTime,
            CreatedByUserId = createdByUserId,
        };

        var build = contract.BuildChildren(
            stations, operationTypes, services, manpowers, tools, materials, generalSupports,
            cancellationPlan, delayPlan);
        if (build.IsFailure) return build.Error;

        var advanceBuild = contract.BuildAdvancePayments(advancePayments, operationTypes);
        if (advanceBuild.IsFailure) return advanceBuild.Error;

        contract.Status = ComputeAutomaticStatus(period, now);

        contract.RaiseDomainEvent(new ContractCreatedEvent(contractId, createdByUserId));
        return contract;
    }

    /// <summary>
    /// Replaces every mutable aspect of an existing contract. Refuses when the contract is
    /// already <c>Terminated</c> or <c>Expired</c>. Preserves any partially-consumed advance
    /// or per-line package balances by id.
    /// </summary>
    public Result Update(
        ContractNo contractNo,
        CustomerSnapshot customer,
        CurrencySnapshot currency,
        ContractPeriod period,
        PaymentTerms paymentTerms,
        bool applyVat,
        bool debriefRequired,
        byte[]? attachment,
        FeesAndRates feesAndRates,
        IReadOnlyList<ContractAdvancePaymentDraft> advancePayments,
        CancellationChargePlan cancellationPlan,
        DelayChargePlan delayPlan,
        IReadOnlyList<StationSnapshot> stations,
        IReadOnlyList<ContractOperationTypeDraft> operationTypes,
        IReadOnlyList<ContractServiceDraft> services,
        IReadOnlyList<ContractManpowerDraft> manpowers,
        IReadOnlyList<ContractToolDraft> tools,
        IReadOnlyList<ContractMaterialDraft> materials,
        IReadOnlyList<ContractGeneralSupportDraft> generalSupports,
        Guid adHocOperationTypeId,
        Guid updatedByUserId,
        DateTimeOffset now)
    {
        if (Status is ContractStatus.Terminated)
            return Error.Conflict("Cannot update a terminated contract.");
        if (Status is ContractStatus.Expired)
            return Error.Conflict("Cannot update an expired contract.");

        var headerCheck = ValidateHeader(
            contractNo, customer, currency, period, paymentTerms,
            feesAndRates, cancellationPlan, delayPlan, updatedByUserId);
        if (headerCheck.IsFailure) return headerCheck.Error;

        var stationsCheck = ValidateStations(stations);
        if (stationsCheck.IsFailure) return stationsCheck.Error;

        var operationTypesCheck = ValidateOperationTypes(operationTypes, adHocOperationTypeId);
        if (operationTypesCheck.IsFailure) return operationTypesCheck.Error;

        var operationTypeIds = operationTypes.Select(o => o.OperationType.OperationTypeId).ToHashSet();

        var servicesCheck = ValidateServices(services, operationTypeIds);
        if (servicesCheck.IsFailure) return servicesCheck.Error;

        var manpowersCheck = ValidateManpowers(manpowers, operationTypeIds);
        if (manpowersCheck.IsFailure) return manpowersCheck.Error;

        var toolsCheck = ValidateTools(tools, operationTypeIds);
        if (toolsCheck.IsFailure) return toolsCheck.Error;

        var materialsCheck = ValidateMaterials(materials, operationTypeIds);
        if (materialsCheck.IsFailure) return materialsCheck.Error;

        var generalSupportsCheck = ValidateGeneralSupports(generalSupports, operationTypeIds);
        if (generalSupportsCheck.IsFailure) return generalSupportsCheck.Error;

        var advancePaymentsCheck = ValidateAdvancePayments(advancePayments, operationTypeIds, adHocOperationTypeId);
        if (advancePaymentsCheck.IsFailure) return advancePaymentsCheck.Error;

        var oldServiceRemainings = _services.ToDictionary(s => s.Id.Value, s => s.PackageRemainingBalance);
        var oldManpowerRemainings = _manpowers.ToDictionary(m => m.Id.Value, m => m.PackageRemainingBalance);
        var oldToolRemainings = _tools.ToDictionary(t => t.Id.Value, t => t.PackageRemainingBalance);
        var oldMaterialRemainings = _materials.ToDictionary(m => m.Id.Value, m => m.PackageRemainingBalance);
        var oldGeneralSupportRemainings = _generalSupports.ToDictionary(g => g.Id.Value, g => g.PackageRemainingBalance);

        // Per-OT remaining balances we need to preserve when rebuilding advance-payment
        // children. Keyed on OperationTypeId (the only identifier guaranteed to round-trip
        // through the wizard, since the row id can be null on a freshly added card).
        var oldAdvanceRemainings = _advancePayments.ToDictionary(
            ap => ap.OperationTypeId,
            ap => (RemainingBalance: ap.Payment.RemainingBalance, RemainingDeposit: ap.Payment.RemainingDeposit));

        ContractNo = contractNo;
        Customer = customer;
        CustomerId = customer.CustomerId;
        Currency = currency;
        CurrencyId = currency.CurrencyId;
        Period = period;
        PaymentTerms = paymentTerms;
        ApplyVat = applyVat;
        DebriefRequired = debriefRequired;
        Attachment = attachment;
        FeesAndRates = feesAndRates;
        CancellationBasis = cancellationPlan.Basis;
        CancellationChargeType = cancellationPlan.ChargeType;
        DelayBasis = delayPlan.Basis;
        DelayChargeType = delayPlan.ChargeType;
        DelayType = delayPlan.DelayType;

        _stations.Clear();
        _operationTypes.Clear();
        _services.Clear();
        _manpowers.Clear();
        _tools.Clear();
        _materials.Clear();
        _generalSupports.Clear();
        _cancellationBrackets.Clear();
        _delayBrackets.Clear();
        _advancePayments.Clear();

        var build = BuildChildren(
            stations, operationTypes, services, manpowers, tools, materials, generalSupports,
            cancellationPlan, delayPlan);
        if (build.IsFailure) return build.Error;

        var advanceBuild = BuildAdvancePayments(advancePayments, operationTypes);
        if (advanceBuild.IsFailure) return advanceBuild.Error;

        // Re-apply the previously consumed amounts so an in-flight package isn't reset to
        // pristine balances on every Update. We rehydrate to the lower of the two
        // remainings to avoid overstating consumption when balance/deposit shrank.
        foreach (var ap in _advancePayments)
        {
            if (!oldAdvanceRemainings.TryGetValue(ap.OperationTypeId, out var prev))
                continue;

            var rehydrated = ScheduledAdvancedPayment.Rehydrate(
                ap.Payment.FlightsCount,
                ap.Payment.FlightCost,
                ap.Payment.Balance,
                ap.Payment.Deposit,
                Money.From(Math.Min(ap.Payment.Balance.Amount, prev.RemainingBalance.Amount)),
                Money.From(Math.Min(ap.Payment.Deposit.Amount, prev.RemainingDeposit.Amount)));
            if (rehydrated.IsSuccess)
                ap.ReplaceWith(rehydrated.Value);
        }

        foreach (var line in _services)
            if (line.Id.Value is var sid && oldServiceRemainings.TryGetValue(sid, out var prev))
                line.PreserveRemainingBalance(prev);

        foreach (var line in _manpowers)
            if (line.Id.Value is var mid && oldManpowerRemainings.TryGetValue(mid, out var prev))
                line.PreserveRemainingBalance(prev);

        foreach (var line in _tools)
            if (line.Id.Value is var tid && oldToolRemainings.TryGetValue(tid, out var prev))
                line.PreserveRemainingBalance(prev);

        foreach (var line in _materials)
            if (line.Id.Value is var mid && oldMaterialRemainings.TryGetValue(mid, out var prev))
                line.PreserveRemainingBalance(prev);

        foreach (var line in _generalSupports)
            if (line.Id.Value is var gid && oldGeneralSupportRemainings.TryGetValue(gid, out var prev))
                line.PreserveRemainingBalance(prev);

        UpdatedAt = now.UtcDateTime;
        UpdatedByUserId = updatedByUserId;

        SyncAutomaticStatus(now);
        RaiseDomainEvent(new ContractUpdatedEvent(Id, updatedByUserId));
        return Result.Success();
    }

    /// <summary>Manual transition from <c>Active</c> only.</summary>
    public Result Suspend(string? reason, Guid byUserId, DateTimeOffset now)
    {
        if (byUserId == Guid.Empty)
            return Error.Validation("Suspend requires a current user.");
        if (string.IsNullOrWhiteSpace(reason))
            return Error.Validation("Suspend reason is required.");
        if (reason.Length > 500)
            return Error.Validation("Suspend reason must not exceed 500 characters.");

        if (Status is not ContractStatus.Active)
            return Error.Conflict("Only an Active contract can be suspended.");

        Status = ContractStatus.Suspended;
        UpdatedAt = now.UtcDateTime;
        UpdatedByUserId = byUserId;
        RaiseDomainEvent(new ContractSuspendedEvent(Id, reason.Trim(), byUserId));
        return Result.Success();
    }

    /// <summary>
    /// Resumes from <c>Suspended</c>; recomputes the new status from the period at
    /// <paramref name="now"/> (Draft / Active / Expired).
    /// </summary>
    public Result Activate(Guid byUserId, DateTimeOffset now)
    {
        if (byUserId == Guid.Empty)
            return Error.Validation("Activate requires a current user.");

        if (Status is not ContractStatus.Suspended)
            return Error.Conflict("Only a Suspended contract can be activated.");

        var newStatus = ComputeAutomaticStatus(Period, now);
        Status = newStatus;
        UpdatedAt = now.UtcDateTime;
        UpdatedByUserId = byUserId;
        RaiseDomainEvent(new ContractResumedEvent(Id, newStatus, byUserId));

        if (newStatus is ContractStatus.Expired)
            RaiseDomainEvent(new ContractExpiredEvent(Id));

        return Result.Success();
    }

    /// <summary>Manual hard exit. Refused from <c>Terminated</c> or <c>Expired</c>.</summary>
    public Result Terminate(string? reason, Guid byUserId, DateTimeOffset atUtc)
    {
        if (Status is ContractStatus.Terminated)
            return Error.Conflict("Contract is already terminated.");
        if (Status is ContractStatus.Expired)
            return Error.Conflict("Cannot terminate an expired contract.");

        var termResult = Termination.Create(reason, atUtc.UtcDateTime, byUserId);
        if (termResult.IsFailure) return termResult.Error;

        Termination = termResult.Value;
        Status = ContractStatus.Terminated;
        UpdatedAt = atUtc.UtcDateTime;
        UpdatedByUserId = byUserId;

        RaiseDomainEvent(new ContractTerminatedEvent(Id, termResult.Value.Reason, byUserId, atUtc.UtcDateTime));
        return Result.Success();
    }

    /// <summary>
    /// Auto Draft↔Active↔Expired transitions. No-op while the contract is Suspended or
    /// Terminated. Raises <c>ContractActivatedEvent(automatic:true)</c> /
    /// <c>ContractExpiredEvent</c> when state changes.
    /// </summary>
    public Result SyncAutomaticStatus(DateTimeOffset now)
    {
        if (Status is ContractStatus.Suspended or ContractStatus.Terminated)
            return Result.Success();

        var target = ComputeAutomaticStatus(Period, now);
        if (target == Status)
            return Result.Success();

        var previous = Status;
        Status = target;

        if (target is ContractStatus.Active && previous is ContractStatus.Draft)
            RaiseDomainEvent(new ContractActivatedEvent(Id, automatic: true, byUserId: null));

        if (target is ContractStatus.Expired && previous is ContractStatus.Active or ContractStatus.Draft)
            RaiseDomainEvent(new ContractExpiredEvent(Id));

        return Result.Success();
    }

    /// <summary>
    /// Marks an "expiring soon" notification as raised at <paramref name="atUtc"/>. Stops the
    /// notification job from re-publishing inside the same alert cadence window.
    /// </summary>
    public void MarkExpiringSoonNotified(DateTime atUtc) => LastExpiringSoonNotificationAt = atUtc;

    /// <summary>
    /// Deducts <paramref name="charge"/> from the per-OT advance payment for
    /// <paramref name="operationTypeId"/>. Returns a <see cref="Conflict"/> error when the
    /// contract has no advance-payment row for that OT — callers must select a
    /// flight/visit's OT before invoking. Raises
    /// <see cref="ContractAdvancePaymentConsumedEvent"/> on every non-trivial deduction so
    /// audit/billing listeners can track consumption per OT.
    /// </summary>
    public Result<AdvanceConsumption> ConsumeAdvance(Guid operationTypeId, Money charge)
    {
        if (charge is null)
            return Error.Validation("Charge is required.");
        if (operationTypeId == Guid.Empty)
            return Error.Validation("Operation type id is required.");

        var row = _advancePayments.FirstOrDefault(ap => ap.OperationTypeId == operationTypeId);
        if (row is null)
            return Error.Conflict($"Contract has no advance payment for operation type '{operationTypeId}'.");

        var consume = row.Payment.Consume(charge);
        if (consume.IsFailure) return consume.Error;

        row.ReplaceWith(consume.Value.UpdatedPayment);

        if (!consume.Value.FromBalance.IsZero || !consume.Value.FromDeposit.IsZero || !consume.Value.Shortfall.IsZero)
        {
            RaiseDomainEvent(new ContractAdvancePaymentConsumedEvent(
                Id,
                operationTypeId,
                consume.Value.FromBalance,
                consume.Value.FromDeposit,
                consume.Value.Shortfall,
                consume.Value.BalanceDepleted,
                consume.Value.DepositDepleted));
        }

        return consume.Value;
    }

    // ---------------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------------

    private static ContractStatus ComputeAutomaticStatus(ContractPeriod period, DateTimeOffset now)
    {
        if (now < period.StartDate) return ContractStatus.Draft;
        if (now > period.ExpiryDate) return ContractStatus.Expired;
        return ContractStatus.Active;
    }

    private static Result ValidateHeader(
        ContractNo contractNo,
        CustomerSnapshot customer,
        CurrencySnapshot currency,
        ContractPeriod period,
        PaymentTerms paymentTerms,
        FeesAndRates feesAndRates,
        CancellationChargePlan cancellationPlan,
        DelayChargePlan delayPlan,
        Guid actorUserId)
    {
        if (contractNo is null) return Error.Validation("Contract number is required.");
        if (customer is null) return Error.Validation("Customer is required.");
        if (currency is null) return Error.Validation("Currency is required.");
        if (period is null) return Error.Validation("Contract period is required.");
        if (feesAndRates is null) return Error.Validation("Fees and rates are required.");
        if (cancellationPlan is null) return Error.Validation("Cancellation charge plan is required.");
        if (delayPlan is null) return Error.Validation("Delay charge plan is required.");
        if (!Enum.IsDefined(paymentTerms)) return Error.Validation("Unknown payment terms.");
        if (actorUserId == Guid.Empty) return Error.Validation("Acting user is required.");

        return Result.Success();
    }

    private static Result ValidateStations(IReadOnlyList<StationSnapshot> stations)
    {
        if (stations is null || stations.Count == 0)
            return Error.Validation("Contract requires at least 1 station.");

        var ids = new HashSet<Guid>();
        foreach (var s in stations)
        {
            if (s is null) return Error.Validation("Station entries cannot be null.");
            if (!ids.Add(s.StationId))
                return Error.Validation($"Station '{s.IataCode}' is listed more than once.");
        }
        return Result.Success();
    }

    private static Result ValidateOperationTypes(
        IReadOnlyList<ContractOperationTypeDraft> operationTypes,
        Guid adHocOperationTypeId)
    {
        if (operationTypes is null || operationTypes.Count == 0)
            return Error.Validation("Contract requires at least 1 operation type.");

        var ids = new HashSet<Guid>();
        foreach (var ot in operationTypes)
        {
            if (ot is null || ot.OperationType is null)
                return Error.Validation("Operation type entries cannot be null.");
            if (ot.OperationType.OperationTypeId == adHocOperationTypeId)
                return Error.Validation("Contracts cannot use the Ad Hoc operation type.");
            if (!ids.Add(ot.OperationType.OperationTypeId))
                return Error.Validation($"Operation type '{ot.OperationType.Name}' is listed more than once.");

            // Per-OT services rules (≥1 service, no AOG mix) live on ContractOperationType.Create.
            // We re-run the same predicate here so the wizard short-circuits before the build phase.
            if (ot.Services is null || ot.Services.Count == 0)
                return Error.Validation(
                    $"Operation type '{ot.OperationType.Name}' must have at least 1 service.");

            var seenSvc = new HashSet<Guid>();
            var anyAog = false;
            var anyNon = false;
            foreach (var svc in ot.Services)
            {
                if (svc is null)
                    return Error.Validation(
                        $"Operation type '{ot.OperationType.Name}' has a null service entry.");
                if (!seenSvc.Add(svc.ServiceId))
                    return Error.Validation(
                        $"Service '{svc.Name}' is listed more than once for operation type '{ot.OperationType.Name}'.");
                if (svc.IsAog) anyAog = true;
                else anyNon = true;
            }
            if (anyAog && anyNon)
                return Error.Validation(
                    $"Operation type '{ot.OperationType.Name}' may have either the AOG service alone "
                    + "or any other set of services — not a mix.");
        }
        return Result.Success();
    }

    /// <summary>
    /// Pricing-line guard for services. Pricing rows are entirely optional (an empty list is
    /// valid — billing falls back to the system default plans). We only enforce that every
    /// row's OT belongs to the contract and that the (OT, Service, AircraftType) tuple is
    /// unique. The AOG-vs-others rule lives on <see cref="ContractOperationType"/>.
    /// </summary>
    private static Result ValidateServices(
        IReadOnlyList<ContractServiceDraft> services,
        IReadOnlySet<Guid> operationTypeIds)
    {
        if (services is null || services.Count == 0)
            return Result.Success();

        foreach (var s in services)
        {
            if (s is null) return Error.Validation("Service entries cannot be null.");
            if (!operationTypeIds.Contains(s.OperationType.OperationTypeId))
                return Error.Validation(
                    $"Service '{s.Service.Name}' references operation type "
                    + $"'{s.OperationType.Name}' which is not part of the contract.");
        }

        var groupedByOt = services.GroupBy(s => s.OperationType.OperationTypeId);
        foreach (var group in groupedByOt)
        {
            var seen = new HashSet<(Guid serviceId, Guid? aircraftTypeId)>();
            foreach (var s in group)
            {
                var key = (s.Service.ServiceId, s.AircraftType?.AircraftTypeId);
                if (!seen.Add(key))
                    return Error.Validation(
                        $"Service '{s.Service.Name}' is listed more than once for operation type "
                        + $"'{s.OperationType.Name}' with the same aircraft type scope.");
            }
        }

        return Result.Success();
    }

    private static Result ValidateManpowers(
        IReadOnlyList<ContractManpowerDraft> manpowers,
        IReadOnlySet<Guid> operationTypeIds)
    {
        if (manpowers is null) return Result.Success();

        foreach (var m in manpowers)
        {
            if (m is null) return Error.Validation("Manpower entries cannot be null.");
            if (!operationTypeIds.Contains(m.OperationType.OperationTypeId))
                return Error.Validation(
                    $"Manpower line for type '{m.ManpowerType.Name}' references operation type "
                    + $"'{m.OperationType.Name}' which is not part of the contract.");
        }

        var groupedByOt = manpowers.GroupBy(m => m.OperationType.OperationTypeId);
        foreach (var group in groupedByOt)
        {
            var seen = new HashSet<Guid>();
            foreach (var m in group)
            {
                if (!seen.Add(m.ManpowerType.ManpowerTypeId))
                    return Error.Validation(
                        $"Manpower type '{m.ManpowerType.Name}' is listed more than once for "
                        + $"operation type '{m.OperationType.Name}'.");
            }
        }

        return Result.Success();
    }

    private static Result ValidateTools(
        IReadOnlyList<ContractToolDraft> tools,
        IReadOnlySet<Guid> operationTypeIds)
    {
        if (tools is null) return Result.Success();

        foreach (var t in tools)
        {
            if (t is null) return Error.Validation("Tool entries cannot be null.");
            if (!operationTypeIds.Contains(t.OperationType.OperationTypeId))
                return Error.Validation(
                    $"Tool line for '{t.Tool.Name}' references operation type "
                    + $"'{t.OperationType.Name}' which is not part of the contract.");
        }

        var groupedByOt = tools.GroupBy(t => t.OperationType.OperationTypeId);
        foreach (var group in groupedByOt)
        {
            var seen = new HashSet<(Guid toolId, Guid? aircraftTypeId)>();
            foreach (var t in group)
            {
                var key = (t.Tool.ToolId, t.AircraftType?.AircraftTypeId);
                if (!seen.Add(key))
                    return Error.Validation(
                        $"Tool '{t.Tool.Name}' is listed more than once for operation type "
                        + $"'{t.OperationType.Name}' with the same aircraft type scope.");
            }
        }

        return Result.Success();
    }

    private static Result ValidateMaterials(
        IReadOnlyList<ContractMaterialDraft> materials,
        IReadOnlySet<Guid> operationTypeIds)
    {
        if (materials is null) return Result.Success();

        foreach (var m in materials)
        {
            if (m is null) return Error.Validation("Material entries cannot be null.");
            if (!operationTypeIds.Contains(m.OperationType.OperationTypeId))
                return Error.Validation(
                    $"Material line for '{m.Material.Name}' references operation type "
                    + $"'{m.OperationType.Name}' which is not part of the contract.");
        }

        var groupedByOt = materials.GroupBy(m => m.OperationType.OperationTypeId);
        foreach (var group in groupedByOt)
        {
            var seen = new HashSet<Guid>();
            foreach (var m in group)
            {
                if (!seen.Add(m.Material.MaterialId))
                    return Error.Validation(
                        $"Material '{m.Material.Name}' is listed more than once for "
                        + $"operation type '{m.OperationType.Name}'.");
            }
        }

        return Result.Success();
    }

    private static Result ValidateGeneralSupports(
        IReadOnlyList<ContractGeneralSupportDraft> generalSupports,
        IReadOnlySet<Guid> operationTypeIds)
    {
        if (generalSupports is null) return Result.Success();

        foreach (var g in generalSupports)
        {
            if (g is null) return Error.Validation("General-support entries cannot be null.");
            if (!operationTypeIds.Contains(g.OperationType.OperationTypeId))
                return Error.Validation(
                    $"General-support line for '{g.GeneralSupport.Name}' references operation type "
                    + $"'{g.OperationType.Name}' which is not part of the contract.");
        }

        var groupedByOt = generalSupports.GroupBy(g => g.OperationType.OperationTypeId);
        foreach (var group in groupedByOt)
        {
            var seen = new HashSet<Guid>();
            foreach (var g in group)
            {
                if (!seen.Add(g.GeneralSupport.GeneralSupportId))
                    return Error.Validation(
                        $"General-support '{g.GeneralSupport.Name}' is listed more than once for "
                        + $"operation type '{g.OperationType.Name}'.");
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates the per-OT advance-payment drafts. Rejects nulls, duplicate OTs, OTs not on
    /// the contract, the AdHoc OT, and invalid money values (delegated to
    /// <see cref="ScheduledAdvancedPayment.Create"/>). Empty list = "no advance payments
    /// configured" — that's allowed.
    /// </summary>
    private static Result ValidateAdvancePayments(
        IReadOnlyList<ContractAdvancePaymentDraft> drafts,
        IReadOnlySet<Guid> operationTypeIds,
        Guid adHocOperationTypeId)
    {
        if (drafts is null) return Result.Success();

        var seen = new HashSet<Guid>();
        foreach (var draft in drafts)
        {
            if (draft is null) return Error.Validation("Advance payment entries cannot be null.");

            if (draft.OperationTypeId == Guid.Empty)
                return Error.Validation("Advance payment operation type is required.");

            if (draft.OperationTypeId == adHocOperationTypeId)
                return Error.Validation("Advance payments cannot target the Ad Hoc operation type.");

            if (!operationTypeIds.Contains(draft.OperationTypeId))
                return Error.Validation(
                    $"Advance payment references operation type '{draft.OperationTypeId}' which is not part of the contract.");

            if (!seen.Add(draft.OperationTypeId))
                return Error.Validation(
                    $"Operation type '{draft.OperationTypeId}' has more than one advance payment row — only one is allowed per OT.");

            // Defer the per-amount checks to the VO's own factory; if it rejects we surface
            // the same message so the wizard's error pipeline shows a single source of truth.
            var moneyCheck = ScheduledAdvancedPayment.Create(
                draft.FlightsCount,
                Money.From(draft.FlightCost),
                Money.From(draft.Balance),
                Money.From(draft.Deposit));
            if (moneyCheck.IsFailure) return moneyCheck.Error;
        }

        return Result.Success();
    }

    /// <summary>
    /// Materialises the per-OT <see cref="ContractAdvancePayment"/> child rows from the
    /// validated drafts. Resolves each draft's <see cref="OperationTypeSnapshot"/> against
    /// the OT list passed to Create/Update so the snapshot is always consistent with the
    /// rest of the contract's per-OT child entities.
    /// </summary>
    private Result BuildAdvancePayments(
        IReadOnlyList<ContractAdvancePaymentDraft> drafts,
        IReadOnlyList<ContractOperationTypeDraft> operationTypes)
    {
        if (drafts is null || drafts.Count == 0) return Result.Success();

        var snapshotById = operationTypes.ToDictionary(o => o.OperationType.OperationTypeId, o => o.OperationType);
        foreach (var draft in drafts)
        {
            if (!snapshotById.TryGetValue(draft.OperationTypeId, out var snapshot))
                return Error.Validation(
                    $"Advance payment references operation type '{draft.OperationTypeId}' which is not part of the contract.");

            var paymentResult = ScheduledAdvancedPayment.Create(
                draft.FlightsCount,
                Money.From(draft.FlightCost),
                Money.From(draft.Balance),
                Money.From(draft.Deposit));
            if (paymentResult.IsFailure) return paymentResult.Error;

            // Reuse the existing entity id when the wizard rehydrated from the saved
            // contract — keeps the DB row stable across Update so consumption history
            // (and any future billing FKs) stay attached instead of being orphaned by a
            // Clear()+INSERT cycle.
            var existingId = draft.ExistingContractAdvancePaymentId is { } eid
                ? ContractAdvancePaymentId.From(eid)
                : null;

            var built = ContractAdvancePayment.Create(Id, existingId, snapshot, paymentResult.Value);
            if (built.IsFailure) return built.Error;

            _advancePayments.Add(built.Value);
        }

        return Result.Success();
    }

    private Result BuildChildren(
        IReadOnlyList<StationSnapshot> stations,
        IReadOnlyList<ContractOperationTypeDraft> operationTypes,
        IReadOnlyList<ContractServiceDraft> services,
        IReadOnlyList<ContractManpowerDraft> manpowers,
        IReadOnlyList<ContractToolDraft> tools,
        IReadOnlyList<ContractMaterialDraft> materials,
        IReadOnlyList<ContractGeneralSupportDraft> generalSupports,
        CancellationChargePlan cancellationPlan,
        DelayChargePlan delayPlan)
    {
        foreach (var s in stations)
            _stations.Add(ContractStation.Create(Id, s));

        foreach (var ot in operationTypes)
        {
            var built = ContractOperationType.Create(Id, ot.OperationType, ot.Services);
            if (built.IsFailure) return built.Error;
            _operationTypes.Add(built.Value);
        }

        for (var i = 0; i < cancellationPlan.Brackets.Count; i++)
        {
            var row = cancellationPlan.Brackets[i];
            _cancellationBrackets.Add(CancellationBracket.Create(Id, row.MinMinutes, row.MaxMinutes, row.Value, i));
        }

        for (var i = 0; i < delayPlan.Brackets.Count; i++)
        {
            var row = delayPlan.Brackets[i];
            _delayBrackets.Add(DelayBracket.Create(Id, row.MinMinutes, row.MaxMinutes, row.Value, i));
        }

        foreach (var draft in services)
        {
            var existingId = draft.ExistingContractServiceId.HasValue
                ? ContractServiceId.From(draft.ExistingContractServiceId.Value)
                : null;
            var built = ContractService.Create(
                Id, existingId,
                draft.OperationType, draft.Service, draft.AircraftType,
                draft.Basis, draft.PackagePaidBalance, draft.Brackets);
            if (built.IsFailure) return built.Error;
            _services.Add(built.Value);
        }

        foreach (var draft in manpowers)
        {
            var existingId = draft.ExistingContractManpowerId.HasValue
                ? ContractManpowerId.From(draft.ExistingContractManpowerId.Value)
                : null;
            var built = ContractManpower.Create(
                Id, existingId,
                draft.OperationType, draft.ManpowerType,
                draft.Basis, draft.PackagePaidBalance, draft.Brackets);
            if (built.IsFailure) return built.Error;
            _manpowers.Add(built.Value);
        }

        foreach (var draft in tools)
        {
            var existingId = draft.ExistingContractToolId.HasValue
                ? ContractToolId.From(draft.ExistingContractToolId.Value)
                : null;
            var built = ContractTool.Create(
                Id, existingId,
                draft.OperationType, draft.Tool, draft.AircraftType,
                draft.Basis, draft.PackagePaidBalance, draft.Brackets);
            if (built.IsFailure) return built.Error;
            _tools.Add(built.Value);
        }

        foreach (var draft in materials)
        {
            var existingId = draft.ExistingContractMaterialId.HasValue
                ? ContractMaterialId.From(draft.ExistingContractMaterialId.Value)
                : null;
            var built = ContractMaterial.Create(
                Id, existingId,
                draft.OperationType, draft.Material,
                draft.Basis, draft.PackagePaidBalance, draft.Brackets);
            if (built.IsFailure) return built.Error;
            _materials.Add(built.Value);
        }

        foreach (var draft in generalSupports)
        {
            var existingId = draft.ExistingContractGeneralSupportId.HasValue
                ? ContractGeneralSupportId.From(draft.ExistingContractGeneralSupportId.Value)
                : null;
            var built = ContractGeneralSupport.Create(
                Id, existingId,
                draft.OperationType, draft.GeneralSupport,
                draft.Basis, draft.PackagePaidBalance, draft.Brackets);
            if (built.IsFailure) return built.Error;
            _generalSupports.Add(built.Value);
        }

        return Result.Success();
    }
}
