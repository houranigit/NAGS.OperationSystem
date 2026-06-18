using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.Pricing.ToolPricePlans.Dialog;

/// <summary>
/// UI scope state for Tool price-plan dialogs. Trimmed to Item + Currency + Basis
/// (no operation-type / aircraft-type, mirroring the flat Store pricing model).
/// </summary>
public sealed class ToolPricePlanScopeFormModel
{
    public Guid? ToolId { get; set; }

    public Guid? CurrencyId { get; set; }

    public PricingBasis Basis { get; set; } = PricingBasis.Flat;
}
