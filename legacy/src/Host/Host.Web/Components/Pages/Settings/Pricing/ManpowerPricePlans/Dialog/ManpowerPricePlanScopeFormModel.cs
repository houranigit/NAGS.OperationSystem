using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.Pricing.ManpowerPricePlans.Dialog;

public sealed class ManpowerPricePlanScopeFormModel
{
    public Guid? ManpowerTypeId { get; set; }

    public Guid? OperationTypeId { get; set; }

    public Guid? CurrencyId { get; set; }

    public PricingBasis Basis { get; set; } = PricingBasis.Duration;
}
