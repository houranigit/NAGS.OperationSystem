using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>Point-in-time copy of a Core currency. Frozen at contract create.</summary>
public sealed class CurrencySnapshot : ValueObject
{
    public Guid CurrencyId { get; private set; }
    public string Code { get; private set; } = null!;

    private CurrencySnapshot() { }

    private CurrencySnapshot(Guid currencyId, string code)
    {
        CurrencyId = currencyId;
        Code = code;
    }

    public static Result<CurrencySnapshot> Create(Guid currencyId, string? code)
    {
        if (currencyId == Guid.Empty)
            return Error.Validation("CurrencyId is required.");

        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation("Currency code is required.");

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
            return Error.Validation("Currency code must be exactly 3 characters.");

        return new CurrencySnapshot(currencyId, normalized);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return CurrencyId;
        yield return Code;
    }
}
