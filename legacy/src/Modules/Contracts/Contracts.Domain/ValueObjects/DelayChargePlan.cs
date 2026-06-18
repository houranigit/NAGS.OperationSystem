using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;
using Contracts.Domain.Enumerations;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Defines how a customer is charged for delays. Same shape as
/// <see cref="CancellationChargePlan"/> plus a <see cref="DelayType"/> dimension that
/// indicates which delay measurement the brackets evaluate.
/// </summary>
public sealed class DelayChargePlan : ValueObject
{
    public DelayType DelayType { get; }
    public DelayChargeBasis Basis { get; }
    public FeeType ChargeType { get; }
    private readonly IReadOnlyList<DelayBracketRow> _brackets;
    public IReadOnlyList<DelayBracketRow> Brackets => _brackets;

    private DelayChargePlan(
        DelayType delayType,
        DelayChargeBasis basis,
        FeeType chargeType,
        IReadOnlyList<DelayBracketRow> brackets)
    {
        DelayType = delayType;
        Basis = basis;
        ChargeType = chargeType;
        _brackets = brackets;
    }

    public static Result<DelayChargePlan> Create(
        DelayType delayType,
        DelayChargeBasis basis,
        FeeType chargeType,
        IEnumerable<DelayBracketRow>? brackets)
    {
        if (!Enum.IsDefined(delayType))
            return Error.Validation($"Unknown delay type '{delayType}'.");
        if (!Enum.IsDefined(basis))
            return Error.Validation($"Unknown delay basis '{basis}'.");
        if (!Enum.IsDefined(chargeType))
            return Error.Validation($"Unknown charge type '{chargeType}'.");

        var rows = brackets?.ToList() ?? [];

        if (basis == DelayChargeBasis.PerDelay)
        {
            if (rows.Count != 1)
                return Error.Validation("PerDelay plan must contain exactly 1 row.");
            var row = rows[0];
            if (row.MinMinutes != 0 || (row.MaxMinutes ?? 0) != 0)
                return Error.Validation(
                    "PerDelay row must use MinMinutes=0 and MaxMinutes=0 as sentinel bounds.");
            var valueCheck = ValidateRowValue(chargeType, row.Value);
            if (valueCheck.IsFailure) return valueCheck.Error;
            return new DelayChargePlan(delayType, basis, chargeType, rows.AsReadOnly());
        }

        var ladderValidation = ValidateLadder(rows, chargeType);
        if (ladderValidation.IsFailure) return ladderValidation.Error;

        return new DelayChargePlan(delayType, basis, chargeType, rows.AsReadOnly());
    }

    private static Result ValidateRowValue(FeeType chargeType, decimal value)
    {
        if (value < 0m)
            return Error.Validation("Bracket value cannot be negative.");
        if (chargeType == FeeType.Percentage && value > 100m)
            return Error.Validation("Percentage bracket value cannot exceed 100.");
        return Result.Success();
    }

    private static Result ValidateLadder(IReadOnlyList<DelayBracketRow> rows, FeeType chargeType)
    {
        if (rows.Count == 0)
            return Error.Validation("Brackets plan requires at least 1 row.");

        if (rows[0].MinMinutes != 0)
            return Error.Validation("First bracket must start at minute 0.");

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var valueCheck = ValidateRowValue(chargeType, row.Value);
            if (valueCheck.IsFailure) return valueCheck.Error;

            if (row.MinMinutes < 0)
                return Error.Validation("Bracket MinMinutes cannot be negative.");

            if (row.MaxMinutes is not null && row.MaxMinutes <= row.MinMinutes)
                return Error.Validation("Bracket MaxMinutes must be greater than MinMinutes.");

            var isLast = i == rows.Count - 1;
            if (!isLast && row.MaxMinutes is null)
                return Error.Validation("Only the last bracket may be open-ended.");

            if (i > 0)
            {
                var previous = rows[i - 1];
                if (previous.MaxMinutes is int pMax && row.MinMinutes < pMax)
                    return Error.Validation(
                        "Each bracket's minimum must be greater than or equal to the previous "
                        + "bracket's maximum. Waiver minutes may fall in any gap in between.");
            }
        }

        return Result.Success();
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return DelayType;
        yield return Basis;
        yield return ChargeType;
        foreach (var row in _brackets)
        {
            yield return row.MinMinutes;
            yield return row.MaxMinutes ?? -1;
            yield return row.Value;
        }
    }
}
