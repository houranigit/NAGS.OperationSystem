namespace Host.Web.Components.Pages.Settings.Pricing.Shared;

/// <summary>Mutable bracket row for price-plan dialogs (min/max/value only; block & billing are plan-wide).</summary>
public sealed class PriceBracketRowState
{
    public int MinMinutes { get; set; }
    public int? MaxMinutes { get; set; }
    public decimal Value { get; set; }
}
