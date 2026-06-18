namespace Store.Domain.Enumerations;

/// <summary>
/// How a Store price plan computes a billable amount. Cloned from
/// <c>Core.Domain.Enumerations.PricingBasis</c> with identical numeric values so the same
/// shared UI editor and grid filters keep working across modules.
/// </summary>
public enum PricingBasis
{
    Duration = 1,
    Flat = 2
}
