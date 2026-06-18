using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.Pricing.ServicePricePlans.Dialog;

public sealed class ServicePricePlanScopeFormModel
{
    public Guid? ServiceId { get; set; }

    public Guid? OperationTypeId { get; set; }

    public Guid? AircraftTypeId { get; set; }

    public Guid? CurrencyId { get; set; }

    public PricingBasis Basis { get; set; } = PricingBasis.Duration;
}
