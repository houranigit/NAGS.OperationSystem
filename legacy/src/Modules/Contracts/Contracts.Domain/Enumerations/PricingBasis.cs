namespace Contracts.Domain.Enumerations;

/// <summary>
/// How a price plan's brackets are interpreted at billing time. Mirror of
/// <c>Core.Domain.Enumerations.PricingBasis</c> — values must stay in sync.
/// </summary>
public enum PricingBasis
{
    /// <summary>Tiered ladder evaluated against the operation duration.</summary>
    Duration = 1,

    /// <summary>Single open-ended bracket — flat charge regardless of duration.</summary>
    Flat = 2
}
