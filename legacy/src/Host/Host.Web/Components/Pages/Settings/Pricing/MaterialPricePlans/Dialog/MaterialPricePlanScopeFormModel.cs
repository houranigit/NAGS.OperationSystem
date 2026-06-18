using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.Pricing.MaterialPricePlans.Dialog;

public sealed class MaterialPricePlanScopeFormModel
{
    public Guid? MaterialId { get; set; }

    public Guid? CurrencyId { get; set; }

    public PricingBasis Basis { get; set; } = PricingBasis.Flat;
}
