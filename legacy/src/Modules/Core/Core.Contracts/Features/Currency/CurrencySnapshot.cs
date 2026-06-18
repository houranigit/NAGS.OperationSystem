namespace Core.Contracts.Features.Currency;

/// <summary>Lean read-model used by dependent modules.</summary>
public sealed record CurrencySnapshot(
    Guid CurrencyId,
    string Code);
