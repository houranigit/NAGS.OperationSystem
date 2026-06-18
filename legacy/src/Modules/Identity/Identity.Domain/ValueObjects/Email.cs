using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Identity.Domain.ValueObjects;

public sealed class Email : ValueObject
{
    public string Value { get; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Error.Validation("Email is required.");

        var trimmed = raw.Trim().ToLowerInvariant();

        if (trimmed.Length > 254)
            return Error.Validation("Email must not exceed 254 characters.");

        var atIndex = trimmed.IndexOf('@');
        if (atIndex <= 0)
            return Error.Validation("Email must contain '@'.");

        var domain = trimmed[(atIndex + 1)..];
        if (!domain.Contains('.'))
            return Error.Validation("Email domain must contain a dot.");

        return new Email(trimmed);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
