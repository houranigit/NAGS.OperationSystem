namespace Store.Domain.Enumerations;

/// <summary>
/// Per-bracket billing rule. Cloned from <c>Core.Domain.Enumerations.BracketBillingMode</c> with
/// identical numeric values so int-cast cross-module conversions remain safe.
/// </summary>
public enum BracketBillingMode
{
    Floor = 1,
    Ceiling = 2,
    ProRated = 3
}
