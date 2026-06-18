using BuildingBlocks.Domain.Results;
using Core.Domain.Enumerations;

namespace Core.Domain.Pricing;

/// <summary>Shared invariant checks for bracket ladders (duration vs flat).</summary>
public static class PricePlanValidator
{
    public static Result Validate(PricingBasis basis, IReadOnlyList<PriceBracket> brackets)
    {
        if (!Enum.IsDefined(basis))
            return Error.Validation($"Unknown pricing basis '{basis}'.");

        if (brackets is null || brackets.Count == 0)
            return Error.Validation("A price plan requires at least 1 bracket.");

        return basis == PricingBasis.Flat ? ValidateFlat(brackets) : ValidateDurationLadder(brackets);
    }

    private static Result ValidateFlat(IReadOnlyList<PriceBracket> brackets)
    {
        if (brackets.Count != 1)
            return Error.Validation("Flat basis plan must contain exactly 1 bracket.");

        var row = brackets[0];

        if (row.MinMinutes != 0)
            return Error.Validation("Flat bracket must use MinMinutes=0.");
        if (row.MaxMinutes is not null)
            return Error.Validation("Flat bracket must use MaxMinutes=null (open-ended).");
        if (row.BlockSize != 1)
            return Error.Validation("Flat bracket must use BlockSize=1.");
        if (row.BillingMode != BracketBillingMode.ProRated)
            return Error.Validation("Flat bracket must use BillingMode=ProRated.");

        return ValidateRowValue(row.Value);
    }

    private static Result ValidateDurationLadder(IReadOnlyList<PriceBracket> brackets)
    {
        if (brackets[0].MinMinutes != 0)
            return Error.Validation("First bracket must start at minute 0.");

        for (var i = 0; i < brackets.Count; i++)
        {
            var row = brackets[i];

            var valueCheck = ValidateRowValue(row.Value);
            if (valueCheck.IsFailure) return valueCheck.Error;

            if (!Enum.IsDefined(row.BillingMode))
                return Error.Validation($"Unknown bracket billing mode '{row.BillingMode}'.");

            if (row.MinMinutes < 0)
                return Error.Validation("Bracket MinMinutes cannot be negative.");

            if (row.MaxMinutes is not null && row.MaxMinutes <= row.MinMinutes)
                return Error.Validation("Bracket MaxMinutes must be greater than MinMinutes.");

            if (row.BlockSize <= 0)
                return Error.Validation("Bracket BlockSize must be greater than zero.");

            if (row.MaxMinutes is int max && row.BlockSize > (max - row.MinMinutes))
                return Error.Validation(
                    "Bracket BlockSize cannot exceed the bracket's minute span (MaxMinutes - MinMinutes).");

            if (i > 0)
            {
                var previous = brackets[i - 1];
                if (previous.MaxMinutes is int pMax && row.MinMinutes < pMax)
                    return Error.Validation(
                        "Each tier's minimum must be greater than or equal to the previous tier's maximum.");
            }

            var isLast = i == brackets.Count - 1;
            if (!isLast && row.MaxMinutes is null)
                return Error.Validation("Only the last bracket may be open-ended.");
        }

        return Result.Success();
    }

    private static Result ValidateRowValue(decimal value)
    {
        if (value < 0m)
            return Error.Validation("Bracket value cannot be negative.");
        return Result.Success();
    }
}
