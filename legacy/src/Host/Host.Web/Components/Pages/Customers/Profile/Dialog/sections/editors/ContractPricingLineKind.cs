namespace Host.Web.Components.Pages.Customers.Profile.Dialog.Sections.Editors;

/// <summary>
/// Identifies which pricing-line family a step is editing — drives copy ("Add service" /
/// "Add manpower" / …), the lookup list shown in the item picker and whether the
/// aircraft-type column is enabled. The form model uses the same five collections, so the
/// step component switches behaviour entirely from this enum.
/// </summary>
public enum ContractPricingLineKind
{
    Service,
    Manpower,
    Tool,
    Material,
    GeneralSupport
}
