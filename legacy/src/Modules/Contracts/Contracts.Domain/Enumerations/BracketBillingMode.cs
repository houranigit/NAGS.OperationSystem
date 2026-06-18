namespace Contracts.Domain.Enumerations;

/// <summary>
/// How partial minutes inside a bracket are charged. Mirror of
/// <c>Core.Domain.Enumerations.BracketBillingMode</c> — values must stay in sync.
/// </summary>
public enum BracketBillingMode
{
    /// <summary>Round down to the nearest BlockSize.</summary>
    Floor = 1,

    /// <summary>Round up to the nearest BlockSize.</summary>
    Ceiling = 2,

    /// <summary>Charge proportionally to the actual minutes.</summary>
    ProRated = 3
}
