using Core.Contracts.Features.Currency;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.Pricing;
using Core.Contracts.Features.Service;
using Host.Web.Services;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;

namespace Host.Web.Components.Pages.Customers.Profile.Dialog;

/// <summary>
/// Shared lookup state for the contract Add / Edit / Duplicate dialogs.
/// Each dialog instantiates one of these and triggers <see cref="LoadAllAsync"/> on first render
/// so all 9 lookup lists stream in parallel and dropdown skeletons flip individually as they
/// land — same pattern as <c>CustomerAddDialog</c>'s country lookup, just multiplied.
/// </summary>
public sealed class ContractDialogLookups
{
    private readonly string _platformCurrencyCode;

    /// <param name="platformCurrencyCode">
    /// 3-letter ISO platform currency (e.g. <c>SAR</c>) — required by the contract-only
    /// currency lookup so the list can be filtered to "platform + has-rate-to-platform".
    /// Pass <see cref="Configuration.PlatformSettings.CurrencyCode"/> from the host dialog.
    /// </param>
    public ContractDialogLookups(string platformCurrencyCode)
    {
        _platformCurrencyCode = platformCurrencyCode;
    }

    public sealed record DropdownOption(Guid Id, string Name, string DisplayLabel);

    public List<DropdownOption> Currencies { get; private set; } = new();
    public List<DropdownOption> Stations { get; private set; } = new();
    public List<DropdownOption> OperationTypes { get; private set; } = new();
    public List<DropdownOption> Services { get; private set; } = new();
    public List<DropdownOption> ManpowerTypes { get; private set; } = new();
    public List<DropdownOption> AircraftTypes { get; private set; } = new();
    public List<DropdownOption> Tools { get; private set; } = new();
    public List<DropdownOption> Materials { get; private set; } = new();
    public List<DropdownOption> GeneralSupports { get; private set; } = new();

    /// <summary>
    /// Service select-options enriched with their system-default plan(s). Powers the wizard's
    /// "select service → defaulted brackets" flow. Cached separately from the
    /// <see cref="Services"/> dropdown projection (which is derived from the same load).
    /// </summary>
    public IReadOnlyList<ServiceWithPricePlanSelectOption> ServicesWithPlans { get; private set; }
        = Array.Empty<ServiceWithPricePlanSelectOption>();

    public IReadOnlyList<ManpowerTypeWithPricePlanSelectOption> ManpowerTypesWithPlans { get; private set; }
        = Array.Empty<ManpowerTypeWithPricePlanSelectOption>();

    public IReadOnlyList<ToolWithPricePlanSelectOption> ToolsWithPlans { get; private set; }
        = Array.Empty<ToolWithPricePlanSelectOption>();

    public IReadOnlyList<MaterialWithPricePlanSelectOption> MaterialsWithPlans { get; private set; }
        = Array.Empty<MaterialWithPricePlanSelectOption>();

    public IReadOnlyList<GeneralSupportWithPricePlanSelectOption> GeneralSupportsWithPlans { get; private set; }
        = Array.Empty<GeneralSupportWithPricePlanSelectOption>();

    /// <summary>
    /// Exchange rates touching the currently-selected contract currency (both directions).
    /// Empty until <see cref="LoadRatesForCurrencyAsync"/> has been called for a contract
    /// currency. The wizard uses this list (via <see cref="GetRateMapTo"/>) to convert plan
    /// prices from the plan currency into the contract currency.
    /// </summary>
    public IReadOnlyList<ExchangeRateRowDto> ExchangeRates { get; private set; }
        = Array.Empty<ExchangeRateRowDto>();

    /// <summary>The contract currency that <see cref="ExchangeRates"/> was last loaded for.</summary>
    public Guid? ExchangeRatesForContractCurrencyId { get; private set; }

    public bool CurrenciesLoading { get; private set; } = true;
    public bool StationsLoading { get; private set; } = true;
    public bool OperationTypesLoading { get; private set; } = true;
    public bool ServicesLoading { get; private set; } = true;
    public bool ManpowerTypesLoading { get; private set; } = true;
    public bool AircraftTypesLoading { get; private set; } = true;
    public bool ToolsLoading { get; private set; } = true;
    public bool MaterialsLoading { get; private set; } = true;
    public bool GeneralSupportsLoading { get; private set; } = true;

    /// <summary>
    /// True while any lookup list is still loading. Used by the dialog footer to disable Save
    /// until everything the form binds against is hydrated (avoids an empty dropdown wiping a value).
    /// </summary>
    public bool AnyLoading =>
        CurrenciesLoading || StationsLoading || OperationTypesLoading || ServicesLoading
        || ManpowerTypesLoading || AircraftTypesLoading || ToolsLoading || MaterialsLoading
        || GeneralSupportsLoading;

    /// <summary>
    /// Looks up the Core <c>PricePlanScopeOption</c>s registered for a given service. Returns
    /// an empty list if the service id is unknown or has no system-default plans yet — the
    /// per-line dialog falls back to a zeroed flat row in that case.
    /// </summary>
    public IReadOnlyList<PricePlanScopeOption> GetServicePlans(Guid serviceId) =>
        ServicesWithPlans.FirstOrDefault(x => x.Id == serviceId)?.Plans
            ?? Array.Empty<PricePlanScopeOption>();

    public IReadOnlyList<PricePlanScopeOption> GetManpowerTypePlans(Guid manpowerTypeId) =>
        ManpowerTypesWithPlans.FirstOrDefault(x => x.Id == manpowerTypeId)?.Plans
            ?? Array.Empty<PricePlanScopeOption>();

    public IReadOnlyList<Store.Contracts.Features.Pricing.PricePlanScopeOption> GetToolPlans(Guid toolId) =>
        ToolsWithPlans.FirstOrDefault(x => x.Id == toolId)?.Plans
            ?? Array.Empty<Store.Contracts.Features.Pricing.PricePlanScopeOption>();

    public IReadOnlyList<Store.Contracts.Features.Pricing.PricePlanScopeOption> GetMaterialPlans(Guid materialId) =>
        MaterialsWithPlans.FirstOrDefault(x => x.Id == materialId)?.Plans
            ?? Array.Empty<Store.Contracts.Features.Pricing.PricePlanScopeOption>();

    public IReadOnlyList<Store.Contracts.Features.Pricing.PricePlanScopeOption> GetGeneralSupportPlans(Guid id) =>
        GeneralSupportsWithPlans.FirstOrDefault(x => x.Id == id)?.Plans
            ?? Array.Empty<Store.Contracts.Features.Pricing.PricePlanScopeOption>();

    /// <summary>
    /// Host-neutral plan lookup the per-line dialog actually consumes — converts the
    /// module-flavoured <see cref="PricePlanScopeOption"/> records into
    /// <see cref="DialogPricePlanScopeOption"/> so the dialog component doesn't need to
    /// know the difference between Core and Store DTOs. <c>null</c>-safe: returns an empty
    /// list when the item id has no matching plan.
    /// </summary>
    public IReadOnlyList<DialogPricePlanScopeOption> GetServiceDialogPlans(Guid serviceId) =>
        GetServicePlans(serviceId).Select(DialogPricePlanScopeOption.From).ToList();

    public IReadOnlyList<DialogPricePlanScopeOption> GetManpowerTypeDialogPlans(Guid manpowerTypeId) =>
        GetManpowerTypePlans(manpowerTypeId).Select(DialogPricePlanScopeOption.From).ToList();

    public IReadOnlyList<DialogPricePlanScopeOption> GetToolDialogPlans(Guid toolId) =>
        GetToolPlans(toolId).Select(DialogPricePlanScopeOption.From).ToList();

    public IReadOnlyList<DialogPricePlanScopeOption> GetMaterialDialogPlans(Guid materialId) =>
        GetMaterialPlans(materialId).Select(DialogPricePlanScopeOption.From).ToList();

    public IReadOnlyList<DialogPricePlanScopeOption> GetGeneralSupportDialogPlans(Guid id) =>
        GetGeneralSupportPlans(id).Select(DialogPricePlanScopeOption.From).ToList();

    /// <summary>
    /// Builds a "plan currency id → multiplier into <paramref name="contractCurrencyId"/>"
    /// dictionary from <see cref="ExchangeRates"/>. Same-currency keys map to <c>1m</c>
    /// (identity). Used by the per-line dialog to convert plan brackets when seeding
    /// defaults. Currencies missing a rate are absent from the map; callers must default to
    /// <c>1m</c> and surface a warning where appropriate.
    /// </summary>
    public IReadOnlyDictionary<Guid, decimal> GetRateMapTo(Guid contractCurrencyId)
    {
        var map = new Dictionary<Guid, decimal> { [contractCurrencyId] = 1m };
        foreach (var row in ExchangeRates)
        {
            if (row.ToCurrencyId == contractCurrencyId)
                map[row.FromCurrencyId] = row.Rate;
        }
        return map;
    }

    /// <summary>
    /// Currency id → 3-letter code map sourced from the contract-currency dropdown plus any
    /// currencies referenced by the loaded <see cref="ExchangeRates"/>. The line dialog
    /// uses this to surface a "converted from XXX" hint when a default plan's source
    /// currency differs from the contract currency.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> GetCurrencyCodes()
    {
        var map = new Dictionary<Guid, string>();
        foreach (var c in Currencies)
            map[c.Id] = c.Name;
        return map;
    }

    /// <summary>
    /// Reloads <see cref="ExchangeRates"/> for the given contract currency. Idempotent: a
    /// repeat call for the same id no-ops. Call from the host dialog whenever the user
    /// changes the contract currency on the Setup step.
    /// </summary>
    public async Task LoadRatesForCurrencyAsync(IScopedMediator mediator, Guid currencyId, CancellationToken ct = default)
    {
        if (currencyId == Guid.Empty)
        {
            ExchangeRates = Array.Empty<ExchangeRateRowDto>();
            ExchangeRatesForContractCurrencyId = null;
            return;
        }

        if (ExchangeRatesForContractCurrencyId == currencyId) return;

        ExchangeRates = await LookupCacheExtensions.GetExchangeRatesForCurrencyAsync(mediator, currencyId, ct);
        ExchangeRatesForContractCurrencyId = currencyId;
    }

    /// <summary>
    /// Kicks off all 9 lookup loads in parallel via <see cref="ILookupCache"/> (de-duped per circuit).
    /// <paramref name="onLookupReady"/> fires after each individual list lands so the dialog can
    /// re-render that section's skeleton → real dropdown without waiting for the slowest call.
    /// </summary>
    public Task LoadAllAsync(IScopedMediator mediator, ILookupCache cache, Func<Task> onLookupReady) =>
        Task.WhenAll(
            LoadCurrenciesAsync(mediator, cache, onLookupReady),
            LoadStationsAsync(mediator, cache, onLookupReady),
            LoadOperationTypesAsync(mediator, cache, onLookupReady),
            LoadServicesAsync(mediator, cache, onLookupReady),
            LoadManpowerTypesAsync(mediator, cache, onLookupReady),
            LoadAircraftTypesAsync(mediator, cache, onLookupReady),
            LoadToolsAsync(mediator, cache, onLookupReady),
            LoadMaterialsAsync(mediator, cache, onLookupReady),
            LoadGeneralSupportsAsync(mediator, cache, onLookupReady));

    private async Task LoadCurrenciesAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetContractCurrenciesAsync(m, _platformCurrencyCode);
            Currencies = items.Select(x => new DropdownOption(x.Id, x.Code, x.Code)).ToList();
        }
        finally { CurrenciesLoading = false; await notify(); }
    }

    private async Task LoadStationsAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetStationsAsync(m);
            Stations = items.Select(x => new DropdownOption(x.Id, x.Name, $"{x.IataCode} — {x.Name}")).ToList();
        }
        finally { StationsLoading = false; await notify(); }
    }

    private async Task LoadOperationTypesAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetContractOperationTypesAsync(m);
            OperationTypes = items.Select(x => new DropdownOption(x.Id, x.Name, x.Name)).ToList();
        }
        finally { OperationTypesLoading = false; await notify(); }
    }

    private async Task LoadServicesAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetServicesWithPlansAsync(m);
            ServicesWithPlans = items;
            Services = items.Select(x => new DropdownOption(x.Id, x.Name, x.Name)).ToList();
        }
        finally { ServicesLoading = false; await notify(); }
    }

    private async Task LoadManpowerTypesAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetManpowerTypesWithPlansAsync(m);
            ManpowerTypesWithPlans = items;
            ManpowerTypes = items.Select(x => new DropdownOption(x.Id, x.Name, x.Name)).ToList();
        }
        finally { ManpowerTypesLoading = false; await notify(); }
    }

    private async Task LoadAircraftTypesAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetAircraftTypesAsync(m);
            AircraftTypes = items.Select(x => new DropdownOption(x.Id, x.Model, x.Model)).ToList();
        }
        finally { AircraftTypesLoading = false; await notify(); }
    }

    private async Task LoadToolsAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetToolsWithPlansAsync(m);
            ToolsWithPlans = items;
            Tools = items.Select(x => new DropdownOption(x.Id, x.Name, x.Name)).ToList();
        }
        finally { ToolsLoading = false; await notify(); }
    }

    private async Task LoadMaterialsAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetMaterialsWithPlansAsync(m);
            MaterialsWithPlans = items;
            Materials = items.Select(x => new DropdownOption(x.Id, x.Name, x.Name)).ToList();
        }
        finally { MaterialsLoading = false; await notify(); }
    }

    private async Task LoadGeneralSupportsAsync(IScopedMediator m, ILookupCache c, Func<Task> notify)
    {
        try
        {
            var items = await c.GetGeneralSupportsWithPlansAsync(m);
            GeneralSupportsWithPlans = items;
            GeneralSupports = items.Select(x => new DropdownOption(x.Id, x.Name, x.Name)).ToList();
        }
        finally { GeneralSupportsLoading = false; await notify(); }
    }
}
