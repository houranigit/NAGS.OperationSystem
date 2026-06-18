using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Identity.Domain.ValueObjects;

public sealed class PasswordHash : ValueObject
{
    public string Value { get; }

    private PasswordHash(string value) => Value = value;

    public static Result<PasswordHash> Create(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return Error.Validation("Password hash cannot be empty.");

        return new PasswordHash(hash);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    // Never reveal the hash in logs or serialization
    public override string ToString() => "***";
}
