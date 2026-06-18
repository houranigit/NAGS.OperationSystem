using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.Pricing.Shared;

public static class PricePlanBasisOptions
{
    public sealed record Item(PricingBasis Value, string Label);

    public static readonly IReadOnlyList<Item> All =
    [
        new(PricingBasis.Duration, "Duration brackets"),
        new(PricingBasis.Flat, "Flat per unit")
    ];
}
