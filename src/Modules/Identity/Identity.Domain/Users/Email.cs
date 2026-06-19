using System.Text.RegularExpressions;
using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Identity.Domain.Users;

/// <summary>A validated, normalized email address. Stored lowercase/trimmed for uniqueness.</summary>
public sealed partial class Email : ValueObject
{
    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation("Email is required.", "Identity.Email.Required");

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > 256)
            return Error.Validation("Email must not exceed 256 characters.", "Identity.Email.TooLong");

        if (!EmailRegex().IsMatch(normalized))
            return Error.Validation("Email format is invalid.", "Identity.Email.Invalid");

        return new Email(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
