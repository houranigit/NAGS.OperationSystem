using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Human-readable contract number, trimmed and upper-cased. Uniqueness is enforced at the
/// repository level via <c>IContractRepository.ExistsByContractNoAsync</c>.
/// </summary>
public sealed class ContractNo : ValueObject
{
    public const int MinLength = 3;
    public const int MaxLength = 30;

    public string Value { get; }

    private ContractNo(string value) => Value = value;

    public static Result<ContractNo> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Error.Validation("Contract number is required.");

        var normalized = raw.Trim().ToUpperInvariant();

        if (normalized.Length < MinLength)
            return Error.Validation($"Contract number must be at least {MinLength} characters.");

        if (normalized.Length > MaxLength)
            return Error.Validation($"Contract number must not exceed {MaxLength} characters.");

        return new ContractNo(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
