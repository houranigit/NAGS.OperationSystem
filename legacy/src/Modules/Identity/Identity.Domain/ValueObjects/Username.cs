using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Identity.Domain.ValueObjects;

public sealed class Username : ValueObject
{
    public string Value { get; }

    private Username(string value) => Value = value;

    public static Result<Username> Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Error.Validation("Username is required.");

        var trimmed = raw.Trim();

        if (trimmed.Length < 3)
            return Error.Validation("Username must be at least 3 characters.");

        if (trimmed.Length > 50)
            return Error.Validation("Username must not exceed 50 characters.");

        return new Username(trimmed);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
