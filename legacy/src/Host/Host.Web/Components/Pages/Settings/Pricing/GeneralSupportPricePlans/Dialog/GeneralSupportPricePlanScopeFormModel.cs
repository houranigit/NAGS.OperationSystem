using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.Pricing.GeneralSupportPricePlans.Dialog;

public sealed class GeneralSupportPricePlanScopeFormModel
{
    public Guid? GeneralSupportId { get; set; }

    public Guid? CurrencyId { get; set; }

    public PricingBasis Basis { get; set; } = PricingBasis.Flat;
}
