using BuildingBlocks.Domain.Results;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Contracts.Domain.Enumerations;

namespace Contracts.Domain.Services;

/// <summary>
/// Variant of <c>Core.Domain.Pricing.PricePlanValidator</c> for contract-scoped price plans.
/// Two key differences from the Core validator:
/// <list type="number">
/// <item><description><b>Waiver gaps allowed</b>: a row's <c>MinMinutes</c> only needs to be
/// &gt;= the previous row's <c>MaxMinutes</c> (Core requires them to be equal).</description></item>
/// <item><description><b>Pre-paid invariant</b>: when <paramref name="hasPackageBalance"/> is
/// true every row must carry a <see cref="ContractPriceBracket.PackagePriceValue"/> in
/// <c>[0 .. PriceValue]</c>. When false no row may carry one.</description></item>
/// </list>
/// </summary>
public static class ContractPricePlanValidator
{
    public static Result Validate(
        PricingBasis basis,
        IReadOnlyList<ContractPriceBracket> brackets,
        bool hasPackageBalance)
    {
        if (!Enum.IsDefined(basis))
            return Error.Validation($"Unknown pricing basis '{basis}'.");

        if (brackets is null || brackets.Count == 0)
            return Error.Validation("A contract price plan requires at least 1 bracket.");

        var ladderResult = basis == PricingBasis.Flat
            ? ValidateFlat(brackets)
            : ValidateDurationLadder(brackets);
        if (ladderResult.IsFailure) return ladderResult;

        return ValidatePackageInvariants(brackets, hasPackageBalance);
    }

    private static Result ValidateFlat(IReadOnlyList<ContractPriceBracket> brackets)
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
        if (row.PriceValue < 0m)
            return Error.Validation("Bracket value cannot be negative.");

        return Result.Success();
    }

    private static Result ValidateDurationLadder(IReadOnlyList<ContractPriceBracket> brackets)
    {
        if (brackets[0].MinMinutes != 0)
            return Error.Validation("First bracket must start at minute 0.");

        for (var i = 0; i < brackets.Count; i++)
        {
            var row = brackets[i];

            if (row.PriceValue < 0m)
                return Error.Validation("Bracket value cannot be negative.");

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

            // Waiver-gap rule: row N must start at or after row N-1's max (NOT strictly equal).
            if (i > 0)
            {
                var previous = brackets[i - 1];
                if (previous.MaxMinutes is int pMax && row.MinMinutes < pMax)
                    return Error.Validation(
                        "Each bracket's minimum must be greater than or equal to the previous "
                        + "bracket's maximum. Waiver minutes may fall in any gap in between.");
            }

            var isLast = i == brackets.Count - 1;
            if (!isLast && row.MaxMinutes is null)
                return Error.Validation("Only the last bracket may be open-ended.");
        }

        return Result.Success();
    }

    private static Result ValidatePackageInvariants(
        IReadOnlyList<ContractPriceBracket> brackets,
        bool hasPackageBalance)
    {
        if (!hasPackageBalance)
        {
            // Disallow stray special prices — a line without a package balance never bills the
            // package value, and storing it would mislead future readers.
            return brackets.Any(b => b.PackagePriceValue.HasValue)
                ? Error.Validation(
                    "Package price values may only be set when the line carries a package paid balance.")
                : Result.Success();
        }

        // With a package balance, every row must have a special price <= the standard price.
        for (var i = 0; i < brackets.Count; i++)
        {
            var row = brackets[i];
            if (!row.PackagePriceValue.HasValue)
                return Error.Validation(
                    "Every bracket must carry a package price value when the line has a package paid balance.");
            if (row.PackagePriceValue.Value < 0m)
                return Error.Validation("Package price value cannot be negative.");
            if (row.PackagePriceValue.Value > row.PriceValue)
                return Error.Validation(
                    "Package price value cannot exceed the regular price value of the same bracket.");
        }

        return Result.Success();
    }
}
