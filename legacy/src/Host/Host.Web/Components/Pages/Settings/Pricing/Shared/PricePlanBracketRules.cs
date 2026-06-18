using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.Pricing.Shared;

public static class PricePlanBracketRules
{
    public static bool IsValid(
        PricingBasis basis,
        IReadOnlyList<PriceBracketRowState> rows,
        int sharedBlockMinutes,
        out string? error)
    {
        error = null;
        if (sharedBlockMinutes < 1)
        {
            error = "Block size must be at least 1 minute.";
            return false;
        }

        if (basis == PricingBasis.Flat)
        {
            if (rows.Count != 1)
            {
                error = "Flat pricing uses exactly one price.";
                return false;
            }

            if (rows[0].MinMinutes != 0)
            {
                error = "Flat tier must start at 0.";
                return false;
            }

            return true;
        }

        if (rows.Count < 1)
        {
            error = "Add at least one duration bracket.";
            return false;
        }

        if (rows[0].MinMinutes != 0)
        {
            error = "First bracket must start at 0 minutes.";
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var isLast = i == rows.Count - 1;

            if (!isLast && row.MaxMinutes is null)
            {
                error = $"Bracket {i + 1}: only the last bracket can be open-ended (∞).";
                return false;
            }

            if (row.MaxMinutes is int max)
            {
                if (max <= row.MinMinutes)
                {
                    error = $"Bracket {i + 1}: “To” must be greater than “From”.";
                    return false;
                }

                var span = max - row.MinMinutes;
                if (sharedBlockMinutes > span)
                {
                    error =
                        $"Bracket {i + 1}: block size ({sharedBlockMinutes}) cannot exceed the bracket span ({span} min).";
                    return false;
                }
            }

            if (i > 0)
            {
                var prevMax = rows[i - 1].MaxMinutes;
                if (prevMax is null)
                {
                    error = "Cannot add a bracket after an open-ended tier.";
                    return false;
                }

                if (row.MinMinutes != prevMax.Value)
                {
                    error =
                        $"Bracket {i + 1}: “From” must equal the previous bracket’s “To” ({prevMax.Value}).";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Returns false when the last row is open-ended (no further brackets allowed).</summary>
    public static bool CanAddBracket(IReadOnlyList<PriceBracketRowState> rows)
    {
        if (rows.Count == 0) return false;
        var last = rows[^1];
        return last.MaxMinutes is int mx && mx > last.MinMinutes;
    }
}
