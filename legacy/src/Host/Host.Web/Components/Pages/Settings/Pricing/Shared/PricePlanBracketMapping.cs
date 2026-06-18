using Core.Contracts.Features.Pricing;
using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.Pricing.Shared;

public static class PricePlanBracketMapping
{
    public static IReadOnlyList<PricePlanBracketInput> ToCommandBrackets(
        PricingBasis basis,
        IReadOnlyList<PriceBracketRowState> rows,
        int sharedBlockMinutes,
        BracketBillingMode sharedBilling)
    {
        if (basis == PricingBasis.Flat)
        {
            var r = rows[0];
            return [new PricePlanBracketInput(0, null, sharedBlockMinutes, r.Value, sharedBilling)];
        }

        return rows
            .Select(b => new PricePlanBracketInput(b.MinMinutes, b.MaxMinutes, sharedBlockMinutes, b.Value, sharedBilling))
            .ToList();
    }
}
