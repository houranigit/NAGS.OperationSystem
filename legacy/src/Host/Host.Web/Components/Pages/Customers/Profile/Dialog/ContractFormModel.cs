using Contracts.Application.Features.Contract.Commands.CreateContract;
using Contracts.Application.Features.Contract.Commands.UpdateContract;
using Contracts.Application.Features.Contract.Shared;
using Contracts.Contracts.Contract;
using Contracts.Domain.Enumerations;
using Core.Contracts.Seeding;

namespace Host.Web.Components.Pages.Customers.Profile.Dialog;

/// <summary>
/// Form-side payload for contract Add / Edit / Duplicate dialogs. Carries every wizard
/// section's state plus the helpers that translate to the Application-layer commands.
/// </summary>
public sealed class ContractFormModel
{
    public Guid? Id { get; set; }

    public string ContractNo { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Guid? CurrencyId { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int ExpiryAlertDays { get; set; } = 30;
    public ExpiryAlertInterval? ExpiryAlertInterval { get; set; } = Contracts.Domain.Enumerations.ExpiryAlertInterval.Monthly;

    public PaymentTerms PaymentTerms { get; set; } = PaymentTerms.Net30;
    public bool ApplyVat { get; set; } = true;

    /// <summary>
    /// When true, work orders billed under this contract require a debrief step before
    /// being approved. Bound to a checkbox on the Setup step.
    /// </summary>
    public bool DebriefRequired { get; set; }

    public byte[]? Attachment { get; set; }

    // -- Fees & rates -----------------------------------------------------
    public FeeRow AdminFee { get; set; } = new();
    public FeeRow DisbursementFee { get; set; } = new();
    public FeeRow HolidayFee { get; set; } = new();
    public FeeRow NightFee { get; set; } = new();
    public FeeRow ReturnToRampDiscount { get; set; } = new();
    public FeeRow OtherDiscount { get; set; } = new();

    // -- Advance payments (per operation type) ----------------------------
    /// <summary>
    /// One row per selected operation type. Rows with <see cref="AdvancePaymentRow.HasAdvancePayment"/>
    /// = false are kept around so the wizard renders an inactive card per OT — only enabled
    /// rows are sent to the server. Kept in sync with <see cref="OperationTypeIds"/> via
    /// <see cref="SyncAdvancePaymentRowsWithOperationTypes"/>.
    /// </summary>
    public List<AdvancePaymentRow> AdvancePayments { get; set; } = new();

    // -- Cancellation plan (toggle + plan) --------------------------------
    /// <summary>
    /// Toggle for the cancellation plan. When false the wizard sends a no-op default
    /// (PerCancel / Fixed / 0) to satisfy the always-required Cancellation field on the command.
    /// </summary>
    public bool EnableCancellationPlan { get; set; }
    public CancellationChargeBasis CancellationBasis { get; set; } = CancellationChargeBasis.PerCancel;
    public FeeType CancellationChargeType { get; set; } = FeeType.Fixed;
    public List<PlanBracketRow> CancellationBrackets { get; set; } = new() { new PlanBracketRow { MinMinutes = 0, MaxMinutes = 0, Value = 0m } };

    // -- Delay plan (toggle + plan) ---------------------------------------
    /// <summary>
    /// Toggle for the delay plan. When false the wizard sends a no-op default
    /// (LateDeparture / PerDelay / Fixed / 0) so the always-required Delay field stays valid.
    /// </summary>
    public bool EnableDelayPlan { get; set; }
    public DelayType DelayType { get; set; } = DelayType.LateDeparture;
    public DelayChargeBasis DelayBasis { get; set; } = DelayChargeBasis.PerDelay;
    public FeeType DelayChargeType { get; set; } = FeeType.Fixed;
    public List<PlanBracketRow> DelayBrackets { get; set; } = new() { new PlanBracketRow { MinMinutes = 0, MaxMinutes = 0, Value = 0m } };

    // -- Coverage ---------------------------------------------------------
    public List<Guid> StationIds { get; set; } = new();

    /// <summary>
    /// One row per operation type the contract covers, with its applicable contract
    /// services. Replaces the old <c>OperationTypeIds</c> list. The wizard's "Operation
    /// types" step binds directly to this collection.
    /// </summary>
    public List<OperationTypeRow> OperationTypeRows { get; set; } = new();

    /// <summary>
    /// Convenience read-only view used by sections that previously consumed the flat OT
    /// ids list (services pricing, manpower pricing, advance payments). Reflects the OT
    /// ids declared on <see cref="OperationTypeRows"/>.
    /// </summary>
    public IReadOnlyList<Guid> OperationTypeIds =>
        OperationTypeRows.Select(r => r.OperationTypeId).ToList();

    // -- Pricing lines ----------------------------------------------------
    public List<PricingLineRow> Services { get; set; } = new();
    public List<PricingLineRow> Manpowers { get; set; } = new();
    public List<PricingLineRow> Tools { get; set; } = new();
    public List<PricingLineRow> Materials { get; set; } = new();
    public List<PricingLineRow> GeneralSupports { get; set; } = new();

    // ---------------------------------------------------------------------
    // Build commands
    // ---------------------------------------------------------------------

    public CreateContractCommand ToCreateCommand() =>
        new(
            ContractNo: ContractNo,
            CustomerId: CustomerId,
            CurrencyId: CurrencyId ?? Guid.Empty,
            Period: BuildPeriod(),
            PaymentTerms: PaymentTerms,
            ApplyVat: ApplyVat,
            DebriefRequired: DebriefRequired,
            Attachment: Attachment,
            FeesAndRates: BuildFeesAndRates(),
            AdvancePayments: BuildAdvancePayments(),
            Cancellation: BuildCancellationPlan(),
            Delay: BuildDelayPlan(),
            StationIds: StationIds.ToList(),
            OperationTypes: BuildOperationTypes(),
            Services: Services.Select(BuildService).ToList(),
            Manpowers: Manpowers.Select(BuildManpower).ToList(),
            Tools: Tools.Select(BuildTool).ToList(),
            Materials: Materials.Select(BuildMaterial).ToList(),
            GeneralSupports: GeneralSupports.Select(BuildGeneralSupport).ToList());

    public UpdateContractCommand ToUpdateCommand()
    {
        if (Id is null) throw new InvalidOperationException("Cannot build update command without Id.");
        return new UpdateContractCommand(
            Id: Id.Value,
            ContractNo: ContractNo,
            CustomerId: CustomerId,
            CurrencyId: CurrencyId ?? Guid.Empty,
            Period: BuildPeriod(),
            PaymentTerms: PaymentTerms,
            ApplyVat: ApplyVat,
            DebriefRequired: DebriefRequired,
            Attachment: Attachment,
            FeesAndRates: BuildFeesAndRates(),
            AdvancePayments: BuildAdvancePayments(),
            Cancellation: BuildCancellationPlan(),
            Delay: BuildDelayPlan(),
            StationIds: StationIds.ToList(),
            OperationTypes: BuildOperationTypes(),
            Services: Services.Select(BuildService).ToList(),
            Manpowers: Manpowers.Select(BuildManpower).ToList(),
            Tools: Tools.Select(BuildTool).ToList(),
            Materials: Materials.Select(BuildMaterial).ToList(),
            GeneralSupports: GeneralSupports.Select(BuildGeneralSupport).ToList());
    }

    private IReadOnlyList<ContractOperationTypeInput> BuildOperationTypes() =>
        OperationTypeRows
            .Select(r => new ContractOperationTypeInput(r.OperationTypeId, r.ServiceIds.ToList()))
            .ToList();

    // ---------------------------------------------------------------------
    // Wizard-side validation helpers (RadzenCustomValidator wires these up)
    // ---------------------------------------------------------------------

    public bool IsContractNoInputValid()
    {
        // Aligns with the domain VO (ContractNo.Create): trimmed, non-empty, 3–30 chars.
        var trimmed = (ContractNo ?? string.Empty).Trim();
        return trimmed.Length is >= 3 and <= 30;
    }

    public bool IsCurrencyIdInputValid() => CurrencyId is { } id && id != Guid.Empty;

    /// <summary>
    /// True when a non-empty customer is selected. Only meaningful for the
    /// duplicate-for-other-customer flow where the wizard exposes a picker; the standard
    /// Add / Edit flows scope the customer from page context and never surface this field.
    /// </summary>
    public bool IsCustomerIdInputValid() => CustomerId != Guid.Empty;

    public bool IsStartDateInputValid() => StartDate.HasValue;

    public bool IsExpiryDateInputValid() =>
        ExpiryDate.HasValue && (StartDate is not { } sd || ExpiryDate.Value > sd);

    public bool IsAlertIntervalInputValid() => ExpiryAlertDays <= 0 || ExpiryAlertInterval is not null;

    public bool AreStationsInputValid() => StationIds.Count >= 1;

    /// <summary>
    /// True when the OT step has at least one row and every row passes
    /// <see cref="IsOperationTypeRowValid"/>: a non-empty OT id, ≥ 1 service, no duplicate
    /// service ids inside the row, and AOG-only-OR-no-AOG (no mix). Domain re-validates on
    /// Save; this gate just blocks Next early.
    /// </summary>
    public bool AreOperationTypesInputValid()
    {
        if (OperationTypeRows.Count == 0) return false;
        var seenOt = new HashSet<Guid>();
        foreach (var row in OperationTypeRows)
        {
            if (!seenOt.Add(row.OperationTypeId)) return false;
            if (!IsOperationTypeRowValid(row)) return false;
        }
        return true;
    }

    /// <summary>Per-row validity predicate exposed to the OperationTypesSection validators.</summary>
    public bool IsOperationTypeRowValid(OperationTypeRow row)
    {
        if (row.OperationTypeId == Guid.Empty) return false;
        if (!IsOperationTypeRowServicesCountValid(row)) return false;
        if (!IsOperationTypeRowServicesExtrasValid(row)) return false;
        return true;
    }

    /// <summary>True when at least one service is selected for the row (OT step UI).</summary>
    public bool IsOperationTypeRowServicesCountValid(OperationTypeRow row) => row.ServiceIds.Count >= 1;

    /// <summary>
    /// True when service ids have no duplicates and are not an AOG + non-AOG mix.
    /// When the row has no services yet, returns true so the count validator owns that message.
    /// </summary>
    public bool IsOperationTypeRowServicesExtrasValid(OperationTypeRow row)
    {
        if (row.ServiceIds.Count == 0) return true;
        if (row.ServiceIds.Distinct().Count() != row.ServiceIds.Count) return false;
        var hasAog = row.ServiceIds.Contains(AogServiceId);
        var hasOthers = row.ServiceIds.Any(id => id != AogServiceId);
        return !(hasAog && hasOthers);
    }

    /// <summary>
    /// Per-row validity check for the Advance-payment step's Radzen validators. Only enabled
    /// rows (<see cref="AdvancePaymentRow.HasAdvancePayment"/> = true) are checked; the
    /// inactive cards never gate Save. Mirrors the domain VO's "balance &gt; 0 / deposit ≥ 0"
    /// rules so the user sees feedback before the server bounces.
    /// </summary>
    public bool IsAdvancePaymentRowValid(AdvancePaymentRow row)
    {
        if (!row.HasAdvancePayment) return true;
        if (row.OperationTypeId == Guid.Empty) return false;
        if (row.FlightsCount <= 0) return false;
        if (row.FlightCost <= 0m) return false;
        if (row.Balance <= 0m) return false;
        if (row.Deposit < 0m) return false;
        return true;
    }

    /// <summary>
    /// Step-level gate used by the wizard's <c>Validate:</c> lambda. Every enabled row must
    /// be individually valid AND the OT must still be on the contract — guards against
    /// stale rows that survived an OT being removed and then re-added before the
    /// SyncAdvancePaymentRowsWithOperationTypes pass ran.
    /// </summary>
    public bool AreAdvancePaymentsInputValid()
    {
        if (AdvancePayments is null) return true;
        var allowed = OperationTypeIds.ToHashSet();
        foreach (var row in AdvancePayments)
        {
            if (!row.HasAdvancePayment) continue;
            if (!allowed.Contains(row.OperationTypeId)) return false;
            if (!IsAdvancePaymentRowValid(row)) return false;
        }
        var enabled = AdvancePayments.Where(r => r.HasAdvancePayment).Select(r => r.OperationTypeId).ToList();
        return enabled.Distinct().Count() == enabled.Count;
    }

    /// <summary>
    /// Reconciles the Advance-payment row list with the current OT-step selection. New OTs
    /// get an inactive row appended; OTs that were removed have their row dropped.
    /// </summary>
    public void SyncAdvancePaymentRowsWithOperationTypes()
    {
        var allowed = OperationTypeIds.ToHashSet();
        AdvancePayments.RemoveAll(r => !allowed.Contains(r.OperationTypeId));
        foreach (var ot in OperationTypeIds)
        {
            if (AdvancePayments.Any(r => r.OperationTypeId == ot)) continue;
            AdvancePayments.Add(new AdvancePaymentRow { OperationTypeId = ot });
        }
    }

    public bool IsFeeInputValid(FeeRow fee)
    {
        if (fee.Value < 0m) return false;
        if (fee.Type == FeeType.Percentage && fee.Value > 100m) return false;
        return true;
    }

    public bool AreCancellationBracketsValid()
    {
        if (!EnableCancellationPlan) return true;
        return ValidatePlanBrackets(CancellationBasis == CancellationChargeBasis.PerCancel, CancellationBrackets, CancellationChargeType);
    }

    public bool AreDelayBracketsValid()
    {
        if (!EnableDelayPlan) return true;
        return ValidatePlanBrackets(DelayBasis == DelayChargeBasis.PerDelay, DelayBrackets, DelayChargeType);
    }

    /// <summary>
    /// Seeded "AOG" service id — used by the Services step's auto-seed and the AOG-alone-per-OT
    /// confirm dialogs. Sourced from <see cref="CoreSeedIds"/> so the host stays in sync with
    /// the Core seeding contract.
    /// </summary>
    public static Guid AogServiceId => CoreSeedIds.AogService;

    /// <summary>
    /// UI gate for the Services pricing step. Service pricing rows are now entirely
    /// optional (an empty list means "use system defaults"). The only rules left are:
    /// (OT, Service, AircraftType) uniqueness, every row's OT must be one of the
    /// contract's OTs, and the per-row bracket ladder must be valid. The
    /// AOG-vs-others rule lives on the OT step and is not re-checked here.
    /// </summary>
    public bool AreServicesInputValid()
    {
        if (Services.Count == 0) return true;

        var dupKeys = Services
            .GroupBy(s => (s.OperationTypeId, s.ItemId, s.AircraftTypeId))
            .Any(g => g.Count() > 1);
        if (dupKeys) return false;

        return Services.All(IsPricingLineRowValid);
    }

    /// <summary>UI gate for the Manpower step — per-OT uniqueness on (OT, ManpowerType) and bracket validity.</summary>
    public bool AreManpowersInputValid() =>
        Manpowers.GroupBy(m => (m.OperationTypeId, m.ItemId)).All(g => g.Count() == 1)
        && Manpowers.All(IsPricingLineRowValid);

    /// <summary>UI gate for the Tools step — per-OT uniqueness on (OT, Tool, AircraftType) and bracket validity.</summary>
    public bool AreToolsInputValid() =>
        Tools.GroupBy(t => (t.OperationTypeId, t.ItemId, t.AircraftTypeId)).All(g => g.Count() == 1)
        && Tools.All(IsPricingLineRowValid);

    /// <summary>UI gate for the Materials step — per-OT uniqueness on (OT, Material) and bracket validity.</summary>
    public bool AreMaterialsInputValid() =>
        Materials.GroupBy(m => (m.OperationTypeId, m.ItemId)).All(g => g.Count() == 1)
        && Materials.All(IsPricingLineRowValid);

    /// <summary>UI gate for the General-supports step — per-OT uniqueness on (OT, Item) and bracket validity.</summary>
    public bool AreGeneralSupportsInputValid() =>
        GeneralSupports.GroupBy(g => (g.OperationTypeId, g.ItemId)).All(g => g.Count() == 1)
        && GeneralSupports.All(IsPricingLineRowValid);

    /// <summary>Per-row validity check used by every line-level <c>AreXInputValid</c>.</summary>
    private bool IsPricingLineRowValid(PricingLineRow row)
    {
        if (row.OperationTypeId == Guid.Empty) return false;
        if (!OperationTypeIds.Contains(row.OperationTypeId)) return false;
        if (row.ItemId == Guid.Empty) return false;

        if (!ValidatePricingBrackets(row)) return false;

        // Domain: PackagePriceValue invariants.
        var hasPaid = row.PackagePaidBalance is { } paid && paid > 0m;
        foreach (var b in row.Brackets)
        {
            if (b.PriceValue < 0m) return false;
            if (hasPaid)
            {
                if (b.PackagePriceValue is not { } pv) return false;
                if (pv < 0m || pv > b.PriceValue) return false;
            }
            else if (b.PackagePriceValue is not null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Pricing-line bracket ladder validity. Mirrors <see cref="ValidatePlanBrackets"/> for
    /// <see cref="PriceBracketRow"/> + <see cref="PricingBasis"/>: Flat → exactly one row;
    /// Duration → contiguous, monotonic, last row is open-ended.
    /// </summary>
    private static bool ValidatePricingBrackets(PricingLineRow row)
    {
        var rows = row.Brackets;
        if (row.Basis == PricingBasis.Flat)
        {
            if (rows.Count != 1) return false;
            var r = rows[0];
            return r.PriceValue >= 0m && r.BlockSize >= 1;
        }

        if (rows.Count == 0) return false;
        if (rows[0].MinMinutes != 0) return false;

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var isLast = i == rows.Count - 1;

            if (r.PriceValue < 0m) return false;
            if (r.MinMinutes < 0) return false;
            if (r.BlockSize < 1) return false;
            if (r.MaxMinutes is int mx && mx <= r.MinMinutes) return false;
            if (!isLast && r.MaxMinutes is null) return false;
            if (i > 0 && rows[i - 1].MaxMinutes is int prevMax && r.MinMinutes < prevMax) return false;
        }

        return true;
    }

    private static bool ValidatePlanBrackets(bool isFlat, IReadOnlyList<PlanBracketRow> rows, FeeType chargeType)
    {
        if (isFlat)
        {
            if (rows.Count != 1) return false;
            var r = rows[0];
            return r.Value >= 0m && (chargeType != FeeType.Percentage || r.Value <= 100m);
        }

        if (rows.Count == 0) return false;
        if (rows[0].MinMinutes != 0) return false;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var isLast = i == rows.Count - 1;

            if (row.Value < 0m) return false;
            if (chargeType == FeeType.Percentage && row.Value > 100m) return false;
            if (row.MinMinutes < 0) return false;
            if (row.MaxMinutes is int mx && mx <= row.MinMinutes) return false;
            if (!isLast && row.MaxMinutes is null) return false;
            if (i > 0 && rows[i - 1].MaxMinutes is int prevMax && row.MinMinutes < prevMax) return false;
        }

        return true;
    }

    public ContractFormModel Clone()
    {
        var clone = (ContractFormModel)MemberwiseClone();
        clone.AdminFee = AdminFee.Clone();
        clone.DisbursementFee = DisbursementFee.Clone();
        clone.HolidayFee = HolidayFee.Clone();
        clone.NightFee = NightFee.Clone();
        clone.ReturnToRampDiscount = ReturnToRampDiscount.Clone();
        clone.OtherDiscount = OtherDiscount.Clone();
        clone.CancellationBrackets = CancellationBrackets.Select(b => b.Clone()).ToList();
        clone.DelayBrackets = DelayBrackets.Select(b => b.Clone()).ToList();
        clone.AdvancePayments = AdvancePayments.Select(b => b.Clone()).ToList();
        clone.StationIds = StationIds.ToList();
        clone.OperationTypeRows = OperationTypeRows.Select(r => r.Clone()).ToList();
        clone.Services = Services.Select(s => s.Clone()).ToList();
        clone.Manpowers = Manpowers.Select(s => s.Clone()).ToList();
        clone.Tools = Tools.Select(s => s.Clone()).ToList();
        clone.Materials = Materials.Select(s => s.Clone()).ToList();
        clone.GeneralSupports = GeneralSupports.Select(s => s.Clone()).ToList();
        return clone;
    }

    // ---------------------------------------------------------------------
    // From-DTO hydration (used by Edit and Duplicate dialogs)
    // ---------------------------------------------------------------------

    public static ContractFormModel FromContractDto(ContractDto dto, bool keepIdentity)
    {
        var model = new ContractFormModel
        {
            Id = keepIdentity ? dto.Id : null,
            ContractNo = keepIdentity ? dto.ContractNo : string.Empty,
            CustomerId = dto.Customer.CustomerId,
            CurrencyId = dto.Currency.CurrencyId,
            StartDate = dto.Period.StartDate.UtcDateTime,
            ExpiryDate = dto.Period.ExpiryDate.UtcDateTime,
            ExpiryAlertDays = dto.Period.ExpiryAlertDays,
            ExpiryAlertInterval = dto.Period.ExpiryAlertInterval,
            PaymentTerms = dto.PaymentTerms,
            ApplyVat = dto.ApplyVat,
            DebriefRequired = dto.DebriefRequired,
            Attachment = dto.Attachment,

            AdminFee = FeeRow.FromFee(dto.Fees.AdminFee),
            DisbursementFee = FeeRow.FromFee(dto.Fees.DisbursementFee),
            HolidayFee = FeeRow.FromFee(dto.Fees.HolidayFee),
            NightFee = FeeRow.FromFee(dto.Fees.NightFee),
            ReturnToRampDiscount = FeeRow.FromFee(dto.Fees.ReturnToRampDiscount),
            OtherDiscount = FeeRow.FromFee(dto.Fees.OtherDiscount),

            CancellationBasis = dto.CancellationPlan.Basis,
            CancellationChargeType = dto.CancellationPlan.ChargeType,
            CancellationBrackets = dto.CancellationPlan.Brackets
                .OrderBy(b => b.SortOrder)
                .Select(b => new PlanBracketRow { MinMinutes = b.MinMinutes, MaxMinutes = b.MaxMinutes, Value = b.Value })
                .ToList(),
            // A plan is considered "enabled" iff at least one bracket has a non-zero charge — matches the
            // no-op default we send when the user keeps the switch off.
            EnableCancellationPlan = dto.CancellationPlan.Brackets.Any(b => b.Value > 0m),

            DelayType = dto.DelayPlan.DelayType,
            DelayBasis = dto.DelayPlan.Basis,
            DelayChargeType = dto.DelayPlan.ChargeType,
            DelayBrackets = dto.DelayPlan.Brackets
                .OrderBy(b => b.SortOrder)
                .Select(b => new PlanBracketRow { MinMinutes = b.MinMinutes, MaxMinutes = b.MaxMinutes, Value = b.Value })
                .ToList(),
            EnableDelayPlan = dto.DelayPlan.Brackets.Any(b => b.Value > 0m),

            StationIds = dto.Stations.Select(s => s.StationId).ToList(),
            OperationTypeRows = dto.OperationTypes
                .Select(o => new OperationTypeRow
                {
                    OperationTypeId = o.OperationType.OperationTypeId,
                    ServiceIds = o.Services.Select(s => s.ServiceId).ToList(),
                })
                .ToList(),
        };

        // Hydrate enabled rows from the saved advance-payments and let the Sync helper
        // append inactive rows for OTs that don't have one yet — keeps the per-OT card
        // grid in lockstep with the Setup step on first paint.
        model.AdvancePayments = dto.AdvancePayments
            .Select(ap => new AdvancePaymentRow
            {
                ExistingId = keepIdentity ? ap.Id : null,
                OperationTypeId = ap.OperationTypeId,
                HasAdvancePayment = true,
                FlightsCount = ap.FlightsCount,
                FlightCost = ap.FlightCost,
                Balance = ap.Balance,
                Deposit = ap.Deposit,
            })
            .ToList();
        model.SyncAdvancePaymentRowsWithOperationTypes();

        model.Services = dto.Services.Select(s => new PricingLineRow
        {
            ExistingId = keepIdentity ? s.Id : null,
            OperationTypeId = s.OperationTypeId,
            ItemId = s.Service.ServiceId,
            AircraftTypeId = s.AircraftType?.AircraftTypeId,
            Basis = s.Basis,
            PackagePaidBalance = s.PackagePaidBalance,
            Brackets = s.Brackets.Select(PriceBracketRow.FromDto).ToList(),
        }).ToList();

        model.Manpowers = dto.Manpowers.Select(m => new PricingLineRow
        {
            ExistingId = keepIdentity ? m.Id : null,
            OperationTypeId = m.OperationTypeId,
            ItemId = m.ManpowerType.ManpowerTypeId,
            Basis = m.Basis,
            PackagePaidBalance = m.PackagePaidBalance,
            Brackets = m.Brackets.Select(PriceBracketRow.FromDto).ToList(),
        }).ToList();

        model.Tools = dto.Tools.Select(t => new PricingLineRow
        {
            ExistingId = keepIdentity ? t.Id : null,
            OperationTypeId = t.OperationTypeId,
            ItemId = t.Tool.ToolId,
            AircraftTypeId = t.AircraftType?.AircraftTypeId,
            Basis = t.Basis,
            PackagePaidBalance = t.PackagePaidBalance,
            Brackets = t.Brackets.Select(PriceBracketRow.FromDto).ToList(),
        }).ToList();

        model.Materials = dto.Materials.Select(m => new PricingLineRow
        {
            ExistingId = keepIdentity ? m.Id : null,
            OperationTypeId = m.OperationTypeId,
            ItemId = m.Material.MaterialId,
            Basis = m.Basis,
            PackagePaidBalance = m.PackagePaidBalance,
            Brackets = m.Brackets.Select(PriceBracketRow.FromDto).ToList(),
        }).ToList();

        model.GeneralSupports = dto.GeneralSupports.Select(g => new PricingLineRow
        {
            ExistingId = keepIdentity ? g.Id : null,
            OperationTypeId = g.OperationTypeId,
            ItemId = g.GeneralSupport.GeneralSupportId,
            Basis = g.Basis,
            PackagePaidBalance = g.PackagePaidBalance,
            Brackets = g.Brackets.Select(PriceBracketRow.FromDto).ToList(),
        }).ToList();

        return model;
    }

    // ---------------------------------------------------------------------
    // Builders
    // ---------------------------------------------------------------------

    private ContractPeriodInput BuildPeriod() =>
        new(
            StartDate: StartDate.HasValue ? new DateTimeOffset(StartDate.Value, TimeSpan.Zero) : DateTimeOffset.UtcNow,
            ExpiryDate: ExpiryDate.HasValue ? new DateTimeOffset(ExpiryDate.Value, TimeSpan.Zero) : DateTimeOffset.UtcNow.AddYears(1),
            ExpiryAlertDays: ExpiryAlertDays,
            ExpiryAlertInterval: ExpiryAlertInterval);

    private FeesAndRatesInput BuildFeesAndRates() =>
        new(
            AdminFee: new FeeInput(AdminFee.Type, AdminFee.Value),
            DisbursementFee: new FeeInput(DisbursementFee.Type, DisbursementFee.Value),
            HolidayFee: new FeeInput(HolidayFee.Type, HolidayFee.Value),
            NightFee: new FeeInput(NightFee.Type, NightFee.Value),
            ReturnToRampDiscount: new FeeInput(ReturnToRampDiscount.Type, ReturnToRampDiscount.Value),
            OtherDiscount: new FeeInput(OtherDiscount.Type, OtherDiscount.Value));

    /// <summary>
    /// Materialises only the enabled advance-payment rows into the command input. Inactive
    /// cards are dropped here — they exist purely so the wizard can render one card per
    /// selected operation type without forcing the user to fill them in.
    /// </summary>
    private IReadOnlyList<AdvancePaymentInput> BuildAdvancePayments() =>
        AdvancePayments
            .Where(r => r.HasAdvancePayment)
            .Select(r => new AdvancePaymentInput(
                OperationTypeId: r.OperationTypeId,
                FlightsCount: r.FlightsCount,
                FlightCost: r.FlightCost,
                Balance: r.Balance,
                Deposit: r.Deposit,
                ExistingContractAdvancePaymentId: r.ExistingId))
            .ToList();

    /// <remarks>
    /// When the cancellation toggle is off the wizard sends a no-op default
    /// (PerCancel / Fixed / single 0-value row) so the always-required Cancellation field
    /// stays valid without surfacing penalty inputs to the user.
    /// </remarks>
    private CancellationPlanInput BuildCancellationPlan()
    {
        if (!EnableCancellationPlan)
        {
            return new CancellationPlanInput(
                Basis: CancellationChargeBasis.PerCancel,
                ChargeType: FeeType.Fixed,
                Brackets: [new CancellationBracketInput(0, 0, 0m)]);
        }

        return new CancellationPlanInput(
            Basis: CancellationBasis,
            ChargeType: CancellationChargeType,
            Brackets: CancellationBrackets
                .Select(b => new CancellationBracketInput(b.MinMinutes, b.MaxMinutes, b.Value))
                .ToList());
    }

    private DelayPlanInput BuildDelayPlan()
    {
        if (!EnableDelayPlan)
        {
            return new DelayPlanInput(
                DelayType: DelayType.LateDeparture,
                Basis: DelayChargeBasis.PerDelay,
                ChargeType: FeeType.Fixed,
                Brackets: [new DelayBracketInput(0, 0, 0m)]);
        }

        return new DelayPlanInput(
            DelayType: DelayType,
            Basis: DelayBasis,
            ChargeType: DelayChargeType,
            Brackets: DelayBrackets
                .Select(b => new DelayBracketInput(b.MinMinutes, b.MaxMinutes, b.Value))
                .ToList());
    }

    private static ContractServiceInput BuildService(PricingLineRow row) =>
        new(
            OperationTypeId: row.OperationTypeId,
            ServiceId: row.ItemId,
            AircraftTypeId: row.AircraftTypeId,
            Basis: row.Basis,
            PackagePaidBalance: row.PackagePaidBalance,
            Brackets: row.Brackets.Select(b => b.ToInput()).ToList(),
            ExistingContractServiceId: row.ExistingId);

    private static ContractManpowerInput BuildManpower(PricingLineRow row) =>
        new(
            OperationTypeId: row.OperationTypeId,
            ManpowerTypeId: row.ItemId,
            Basis: row.Basis,
            PackagePaidBalance: row.PackagePaidBalance,
            Brackets: row.Brackets.Select(b => b.ToInput()).ToList(),
            ExistingContractManpowerId: row.ExistingId);

    private static ContractToolInput BuildTool(PricingLineRow row) =>
        new(
            OperationTypeId: row.OperationTypeId,
            ToolId: row.ItemId,
            AircraftTypeId: row.AircraftTypeId,
            Basis: row.Basis,
            PackagePaidBalance: row.PackagePaidBalance,
            Brackets: row.Brackets.Select(b => b.ToInput()).ToList(),
            ExistingContractToolId: row.ExistingId);

    private static ContractMaterialInput BuildMaterial(PricingLineRow row) =>
        new(
            OperationTypeId: row.OperationTypeId,
            MaterialId: row.ItemId,
            Basis: row.Basis,
            PackagePaidBalance: row.PackagePaidBalance,
            Brackets: row.Brackets.Select(b => b.ToInput()).ToList(),
            ExistingContractMaterialId: row.ExistingId);

    private static ContractGeneralSupportInput BuildGeneralSupport(PricingLineRow row) =>
        new(
            OperationTypeId: row.OperationTypeId,
            GeneralSupportId: row.ItemId,
            Basis: row.Basis,
            PackagePaidBalance: row.PackagePaidBalance,
            Brackets: row.Brackets.Select(b => b.ToInput()).ToList(),
            ExistingContractGeneralSupportId: row.ExistingId);

    // ---------------------------------------------------------------------
    // Sub-models
    // ---------------------------------------------------------------------

    /// <summary>
    /// One row per (potentially) selected operation type for the Advance-payment step. The
    /// wizard always renders a card per OT — <see cref="HasAdvancePayment"/> drives whether
    /// the body is enabled and whether the row is sent to the server.
    /// </summary>
    public sealed class AdvancePaymentRow
    {
        public Guid? ExistingId { get; set; }
        public Guid OperationTypeId { get; set; }
        public bool HasAdvancePayment { get; set; }
        public int FlightsCount { get; set; } = 1;
        public decimal FlightCost { get; set; }
        public decimal Balance { get; set; }
        public decimal Deposit { get; set; }

        public AdvancePaymentRow Clone() => new()
        {
            ExistingId = ExistingId,
            OperationTypeId = OperationTypeId,
            HasAdvancePayment = HasAdvancePayment,
            FlightsCount = FlightsCount,
            FlightCost = FlightCost,
            Balance = Balance,
            Deposit = Deposit,
        };
    }

    /// <summary>
    /// One row on the wizard's "Operation types" step: an OT plus the contract services
    /// declared as billable for flights under that OT. Validation enforces ≥ 1 service
    /// and AOG-only OR no-AOG (no mix); see <see cref="IsOperationTypeRowValid"/>.
    /// </summary>
    public sealed class OperationTypeRow
    {
        public Guid OperationTypeId { get; set; }
        public List<Guid> ServiceIds { get; set; } = new();

        public OperationTypeRow Clone() => new()
        {
            OperationTypeId = OperationTypeId,
            ServiceIds = ServiceIds.ToList(),
        };
    }

    public sealed class FeeRow
    {
        public FeeType Type { get; set; } = FeeType.Fixed;
        public decimal Value { get; set; } = 0m;

        public FeeRow Clone() => new() { Type = Type, Value = Value };

        public static FeeRow FromFee(Fee fee) => new() { Type = fee.Type, Value = fee.Value };
    }

    public sealed class PlanBracketRow
    {
        public int MinMinutes { get; set; }
        public int? MaxMinutes { get; set; }
        public decimal Value { get; set; }
        /// <summary>
        /// Optional package price for the row. Only meaningful when the parent line has a
        /// non-zero <c>PackagePaidBalance</c>; the bracket editor surfaces an extra column
        /// for it in that case. Plan brackets used by cancellation/delay leave this null.
        /// </summary>
        public decimal? PackageValue { get; set; }

        public PlanBracketRow Clone() => new() { MinMinutes = MinMinutes, MaxMinutes = MaxMinutes, Value = Value, PackageValue = PackageValue };
    }

    public sealed class PricingLineRow
    {
        public Guid? ExistingId { get; set; }
        public Guid OperationTypeId { get; set; }
        public Guid ItemId { get; set; }
        public Guid? AircraftTypeId { get; set; }
        public PricingBasis Basis { get; set; } = PricingBasis.Flat;
        public decimal? PackagePaidBalance { get; set; }
        public List<PriceBracketRow> Brackets { get; set; } = new() { new PriceBracketRow { MinMinutes = 0, BlockSize = 1, BillingMode = BracketBillingMode.ProRated } };

        public PricingLineRow Clone() => new()
        {
            ExistingId = ExistingId,
            OperationTypeId = OperationTypeId,
            ItemId = ItemId,
            AircraftTypeId = AircraftTypeId,
            Basis = Basis,
            PackagePaidBalance = PackagePaidBalance,
            Brackets = Brackets.Select(b => b.Clone()).ToList(),
        };
    }

    public sealed class PriceBracketRow
    {
        public int MinMinutes { get; set; }
        public int? MaxMinutes { get; set; }
        public int BlockSize { get; set; } = 1;
        public decimal PriceValue { get; set; }
        public decimal? PackagePriceValue { get; set; }
        public BracketBillingMode BillingMode { get; set; } = BracketBillingMode.ProRated;

        public PriceBracketRow Clone() => new()
        {
            MinMinutes = MinMinutes,
            MaxMinutes = MaxMinutes,
            BlockSize = BlockSize,
            PriceValue = PriceValue,
            PackagePriceValue = PackagePriceValue,
            BillingMode = BillingMode,
        };

        public PriceBracketInput ToInput() =>
            new(MinMinutes, MaxMinutes, BlockSize, PriceValue, PackagePriceValue, BillingMode);

        public static PriceBracketRow FromDto(PriceBracket dto) => new()
        {
            MinMinutes = dto.MinMinutes,
            MaxMinutes = dto.MaxMinutes,
            BlockSize = dto.BlockSize,
            PriceValue = dto.PriceValue,
            PackagePriceValue = dto.PackagePriceValue,
            BillingMode = dto.BillingMode,
        };
    }
}
