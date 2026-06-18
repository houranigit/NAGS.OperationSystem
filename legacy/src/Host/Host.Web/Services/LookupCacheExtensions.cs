using Core.Application.Features.AircraftType.Queries.GetPaginatedAircraftTypeSelectOptions;
using Core.Application.Features.Country.Queries.GetPaginatedCountrySelectOptions;
using Core.Application.Features.Currency.Queries.GetExchangeRatesForCurrency;
using Core.Application.Features.Currency.Queries.GetPaginatedCurrencySelectOptions;
using Core.Application.Features.License.Queries.GetPaginatedLicenseSelectOptions;
using Core.Application.Features.ManpowerType.Queries.GetPaginatedManpowerTypeSelectOptions;
using Core.Application.Features.ManpowerType.Queries.GetPaginatedManpowerTypeWithPricePlanSelectOptions;
using Core.Application.Features.OperationType.Queries.GetPaginatedOperationTypeSelectOptions;
using Core.Application.Features.Service.Queries.GetPaginatedServiceSelectOptions;
using Core.Application.Features.Service.Queries.GetPaginatedServiceWithPricePlanSelectOptions;
using Core.Application.Features.Station.Queries.GetPaginatedStationSelectOptions;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Country;
using Core.Contracts.Features.Currency;
using Core.Contracts.Features.License;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Service;
using Core.Contracts.Features.Station;
using Identity.Application.Queries.GetAllRoleSelectOptions;
using Identity.Contracts.Features.Role;
using Store.Application.Features.GeneralSupport.Queries.GetPaginatedGeneralSupportSelectOptions;
using Store.Application.Features.GeneralSupport.Queries.GetPaginatedGeneralSupportWithPricePlanSelectOptions;
using Store.Application.Features.Material.Queries.GetPaginatedMaterialSelectOptions;
using Store.Application.Features.Material.Queries.GetPaginatedMaterialWithPricePlanSelectOptions;
using Store.Application.Features.Tool.Queries.GetPaginatedToolSelectOptions;
using Store.Application.Features.Tool.Queries.GetPaginatedToolWithPricePlanSelectOptions;
using Store.Application.Features.Unit.Queries.GetPaginatedUnitSelectOptions;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;
using Store.Contracts.Features.Unit;

namespace Host.Web.Services;

/// <summary>
/// Contract-only currency option — wrapper record used purely as a distinct
/// <see cref="ILookupCache"/> key so a "ContractCurrencyOnly" load does not poison the
/// global currency cache used by other dialogs.
/// </summary>
public sealed record ContractCurrencySelectOption(Guid Id, string Code);

/// <summary>
/// Contract-only operation-type option — same wrapper-as-cache-key trick so the contract
/// dialog (which excludes Ad Hoc) does not trample the global operation-type cache.
/// </summary>
public sealed record ContractOperationTypeSelectOption(Guid Id, string Name);

/// <summary>
/// One-liner accessors for every lookup list a dialog might need.
/// Each call is de-duplicated by <see cref="ILookupCache"/> and returns the cached copy
/// until either the TTL expires or a grid calls the matching <see cref="ILookupCache.Invalidate{T}"/>.
/// </summary>
/// <remarks>
/// Why not page-size 500 like the callers used to? These endpoints are designed to return
/// the full active set; 500 is the "safe ceiling" upper bound already agreed on in existing code.
/// Keep it identical so behavior is bit-for-bit the same on cache miss.
/// </remarks>
public static class LookupCacheExtensions
{
    private const int LookupPageSize = 500;

    public static Task<IReadOnlyList<ManpowerTypeSelectOption>> GetManpowerTypesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<ManpowerTypeSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedManpowerTypeSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<ManpowerTypeSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<OperationTypeSelectOption>> GetOperationTypesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<OperationTypeSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedOperationTypeSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<OperationTypeSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<CurrencySelectOption>> GetCurrenciesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<CurrencySelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedCurrencySelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<CurrencySelectOption>();
        }, ct);

    public static Task<IReadOnlyList<ServiceSelectOption>> GetServicesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<ServiceSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedServiceSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<ServiceSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<AircraftTypeSelectOption>> GetAircraftTypesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<AircraftTypeSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedAircraftTypeSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<AircraftTypeSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<CountrySelectOption>> GetCountriesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<CountrySelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedCountrySelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<CountrySelectOption>();
        }, ct);

    public static Task<IReadOnlyList<StationSelectOption>> GetStationsAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<StationSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedStationSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<StationSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<LicenseSelectOption>> GetLicensesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<LicenseSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedLicenseSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<LicenseSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<RoleSelectOption>> GetRolesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<RoleSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetAllRoleSelectOptionsQuery(), token);
            return r.IsSuccess ? r.Value : Array.Empty<RoleSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<UnitSelectOption>> GetUnitsAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<UnitSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedUnitSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<UnitSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<MaterialSelectOption>> GetMaterialsAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<MaterialSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedMaterialSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<MaterialSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<GeneralSupportSelectOption>> GetGeneralSupportsAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<GeneralSupportSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedGeneralSupportSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<GeneralSupportSelectOption>();
        }, ct);

    public static Task<IReadOnlyList<ToolSelectOption>> GetToolsAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<ToolSelectOption>(async token =>
        {
            var r = await mediator.Send(new GetPaginatedToolSelectOptionsQuery(1, LookupPageSize), token);
            return r.IsSuccess ? r.Value.Items : Array.Empty<ToolSelectOption>();
        }, ct);

    /// <summary>
    /// Contract-wizard variant of <see cref="GetCurrenciesAsync"/>: returns only the platform
    /// currency plus currencies that already have an exchange rate to the platform — keeping
    /// billing convertible. Cached separately under <see cref="ContractCurrencySelectOption"/>
    /// so this filtered list does not collide with the unrestricted global currency cache.
    /// </summary>
    public static Task<IReadOnlyList<ContractCurrencySelectOption>> GetContractCurrenciesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        string platformCurrencyCode,
        CancellationToken ct = default) =>
        cache.GetAsync<ContractCurrencySelectOption>(async token =>
        {
            var r = await mediator.Send(
                new GetPaginatedCurrencySelectOptionsQuery(
                    Page: 1,
                    PageSize: LookupPageSize,
                    ContractCurrencyOnly: true,
                    PlatformCurrencyCode: platformCurrencyCode),
                token);

            if (!r.IsSuccess) return Array.Empty<ContractCurrencySelectOption>();
            return r.Value.Items
                .Select(x => new ContractCurrencySelectOption(x.Id, x.Code))
                .ToList();
        }, ct);

    /// <summary>
    /// Contract-wizard variant of <see cref="GetOperationTypesAsync"/>: hides the seeded Ad
    /// Hoc operation type since the Contracts domain rejects it on contracts. Cached
    /// separately so this filtered list does not pollute the global OT cache.
    /// </summary>
    public static Task<IReadOnlyList<ContractOperationTypeSelectOption>> GetContractOperationTypesAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<ContractOperationTypeSelectOption>(async token =>
        {
            var r = await mediator.Send(
                new GetPaginatedOperationTypeSelectOptionsQuery(
                    Page: 1,
                    PageSize: LookupPageSize,
                    IncludeAdHoc: false),
                token);

            if (!r.IsSuccess) return Array.Empty<ContractOperationTypeSelectOption>();
            return r.Value.Items
                .Select(x => new ContractOperationTypeSelectOption(x.Id, x.Name))
                .ToList();
        }, ct);

    /// <summary>
    /// Contract-wizard variant of <see cref="GetServicesAsync"/> — surfaces every active
    /// service together with its system-default <c>ServicePricePlan</c>(s) so the
    /// pricing-line dialog can pre-fill brackets when the user picks a service.
    /// </summary>
    public static Task<IReadOnlyList<Core.Contracts.Features.Service.ServiceWithPricePlanSelectOption>> GetServicesWithPlansAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<Core.Contracts.Features.Service.ServiceWithPricePlanSelectOption>(async token =>
        {
            var r = await mediator.Send(
                new GetPaginatedServiceWithPricePlanSelectOptionsQuery(1, LookupPageSize),
                token);
            return r.IsSuccess
                ? r.Value.Items
                : Array.Empty<Core.Contracts.Features.Service.ServiceWithPricePlanSelectOption>();
        }, ct);

    /// <summary>
    /// Contract-wizard variant of <see cref="GetManpowerTypesAsync"/> — surfaces every active
    /// manpower type together with its system-default <c>ManpowerPricePlan</c>(s).
    /// </summary>
    public static Task<IReadOnlyList<Core.Contracts.Features.ManpowerType.ManpowerTypeWithPricePlanSelectOption>> GetManpowerTypesWithPlansAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<Core.Contracts.Features.ManpowerType.ManpowerTypeWithPricePlanSelectOption>(async token =>
        {
            var r = await mediator.Send(
                new GetPaginatedManpowerTypeWithPricePlanSelectOptionsQuery(1, LookupPageSize),
                token);
            return r.IsSuccess
                ? r.Value.Items
                : Array.Empty<Core.Contracts.Features.ManpowerType.ManpowerTypeWithPricePlanSelectOption>();
        }, ct);

    /// <summary>
    /// Contract-wizard variant of <see cref="GetToolsAsync"/> — surfaces every active tool
    /// together with its (typically zero or one) system-default <c>ToolPricePlan</c>.
    /// </summary>
    public static Task<IReadOnlyList<Store.Contracts.Features.Tool.ToolWithPricePlanSelectOption>> GetToolsWithPlansAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<Store.Contracts.Features.Tool.ToolWithPricePlanSelectOption>(async token =>
        {
            var r = await mediator.Send(
                new GetPaginatedToolWithPricePlanSelectOptionsQuery(1, LookupPageSize),
                token);
            return r.IsSuccess
                ? r.Value.Items
                : Array.Empty<Store.Contracts.Features.Tool.ToolWithPricePlanSelectOption>();
        }, ct);

    /// <summary>
    /// Contract-wizard variant of <see cref="GetMaterialsAsync"/> — surfaces every active
    /// material together with its (typically zero or one) system-default <c>MaterialPricePlan</c>.
    /// </summary>
    public static Task<IReadOnlyList<Store.Contracts.Features.Material.MaterialWithPricePlanSelectOption>> GetMaterialsWithPlansAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<Store.Contracts.Features.Material.MaterialWithPricePlanSelectOption>(async token =>
        {
            var r = await mediator.Send(
                new GetPaginatedMaterialWithPricePlanSelectOptionsQuery(1, LookupPageSize),
                token);
            return r.IsSuccess
                ? r.Value.Items
                : Array.Empty<Store.Contracts.Features.Material.MaterialWithPricePlanSelectOption>();
        }, ct);

    /// <summary>
    /// Contract-wizard variant of <see cref="GetGeneralSupportsAsync"/> — surfaces every
    /// active general-support item together with its system-default plan.
    /// </summary>
    public static Task<IReadOnlyList<Store.Contracts.Features.GeneralSupport.GeneralSupportWithPricePlanSelectOption>> GetGeneralSupportsWithPlansAsync(
        this ILookupCache cache,
        IScopedMediator mediator,
        CancellationToken ct = default) =>
        cache.GetAsync<Store.Contracts.Features.GeneralSupport.GeneralSupportWithPricePlanSelectOption>(async token =>
        {
            var r = await mediator.Send(
                new GetPaginatedGeneralSupportWithPricePlanSelectOptionsQuery(1, LookupPageSize),
                token);
            return r.IsSuccess
                ? r.Value.Items
                : Array.Empty<Store.Contracts.Features.GeneralSupport.GeneralSupportWithPricePlanSelectOption>();
        }, ct);

    /// <summary>
    /// Returns every exchange-rate row that touches the given currency (both directions).
    /// Bypasses <see cref="ILookupCache"/> on purpose — the contract wizard re-fetches when
    /// the user changes the contract currency mid-wizard, and the call is small enough that a
    /// per-circuit cache is more confusing than helpful.
    /// </summary>
    public static async Task<IReadOnlyList<Core.Contracts.Features.Currency.ExchangeRateRowDto>> GetExchangeRatesForCurrencyAsync(
        IScopedMediator mediator,
        Guid currencyId,
        CancellationToken ct = default)
    {
        if (currencyId == Guid.Empty)
            return Array.Empty<Core.Contracts.Features.Currency.ExchangeRateRowDto>();

        var r = await mediator.Send(new GetExchangeRatesForCurrencyQuery(currencyId), ct);
        return r.IsSuccess
            ? r.Value
            : Array.Empty<Core.Contracts.Features.Currency.ExchangeRateRowDto>();
    }
}
