using Host.Web.Components.Pages.Settings.Pricing.Shared;

namespace Host.Web.Components.Pages.Settings.Pricing.Shared;

/// <summary>
/// Mirror of <see cref="PricePlanBracketMapping"/> but emitting <see cref="Store.Contracts.Features.Pricing.PricePlanBracketInput"/>
/// rows for the Store module. Underlying enum integer values match across modules so basis /
/// billing mode are converted by an int cast.
/// </summary>
public static class StorePricePlanBracketMapping
{
    public static IReadOnlyList<Store.Contracts.Features.Pricing.PricePlanBracketInput> ToStoreCommandBrackets(
        Core.Domain.Enumerations.PricingBasis basis,
        IReadOnlyList<PriceBracketRowState> rows,
        int sharedBlockMinutes,
        Core.Domain.Enumerations.BracketBillingMode sharedBilling)
    {
        var storeBilling = (Store.Domain.Enumerations.BracketBillingMode)(int)sharedBilling;

        if (basis == Core.Domain.Enumerations.PricingBasis.Flat)
        {
            var r = rows[0];
            return [new Store.Contracts.Features.Pricing.PricePlanBracketInput(0, null, sharedBlockMinutes, r.Value, storeBilling)];
        }

        return rows
            .Select(b => new Store.Contracts.Features.Pricing.PricePlanBracketInput(
                b.MinMinutes,
                b.MaxMinutes,
                sharedBlockMinutes,
                b.Value,
                storeBilling))
            .ToList();
    }

    public static Store.Domain.Enumerations.PricingBasis ToStoreBasis(Core.Domain.Enumerations.PricingBasis basis)
        => (Store.Domain.Enumerations.PricingBasis)(int)basis;

    public static Core.Domain.Enumerations.PricingBasis ToCoreBasis(Store.Domain.Enumerations.PricingBasis basis)
        => (Core.Domain.Enumerations.PricingBasis)(int)basis;

    public static Core.Domain.Enumerations.BracketBillingMode ToCoreBilling(Store.Domain.Enumerations.BracketBillingMode mode)
        => (Core.Domain.Enumerations.BracketBillingMode)(int)mode;
}
