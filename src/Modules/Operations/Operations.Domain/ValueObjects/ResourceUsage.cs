using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;
using MasterData.Contracts.Resources;

namespace Operations.Domain.ValueObjects;

/// <summary>
/// Immutable usage captured for a work-order resource. Exactly one calculation branch is valid:
/// quantity, or an open/closed UTC duration.
/// </summary>
public sealed class ResourceUsage : ValueObject
{
    private ResourceUsage() { }

    private ResourceUsage(decimal? quantity, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        Quantity = quantity;
        FromUtc = fromUtc;
        ToUtc = toUtc;
    }

    public decimal? Quantity { get; private set; }
    public DateTimeOffset? FromUtc { get; private set; }
    public DateTimeOffset? ToUtc { get; private set; }

    public ResourceCalculationType CalculationType =>
        Quantity.HasValue ? ResourceCalculationType.Quantity : ResourceCalculationType.Duration;

    public static Result<ResourceUsage> Create(
        ResourceCalculationType calculationType,
        decimal? quantity,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        if (!Enum.IsDefined(calculationType))
            return Error.Validation("Resource calculation type is invalid.", "Operations.ResourceUsage.CalculationTypeInvalid");

        if (calculationType == ResourceCalculationType.Quantity)
        {
            if (quantity is null || quantity <= 0)
                return Error.Validation("Quantity must be greater than zero.", "Operations.ResourceUsage.QuantityInvalid");
            if (quantity > 9999999999999999.99m || decimal.Round(quantity.Value, 2) != quantity.Value)
                return Error.Validation(
                    "Quantity must fit decimal(18,2) precision (up to 16 whole digits and 2 decimal places).",
                    "Operations.ResourceUsage.QuantityPrecisionInvalid");
            if (fromUtc.HasValue || toUtc.HasValue)
                return Error.Validation("Quantity usage cannot include duration times.", "Operations.ResourceUsage.QuantityHasDuration");

            return new ResourceUsage(quantity, null, null);
        }

        if (quantity.HasValue)
            return Error.Validation("Duration usage cannot include a quantity.", "Operations.ResourceUsage.DurationHasQuantity");
        if (fromUtc is null || fromUtc.Value == default)
            return Error.Validation("Duration usage requires a From time.", "Operations.ResourceUsage.FromRequired");
        if (toUtc is { } to && to == default)
            return Error.Validation("Duration To time must be valid when supplied.", "Operations.ResourceUsage.ToInvalid");
        if (toUtc < fromUtc)
            return Error.Validation("Duration To time cannot be before From time.", "Operations.ResourceUsage.WindowInvalid");

        return new ResourceUsage(
            null,
            fromUtc.Value.ToUniversalTime(),
            toUtc?.ToUniversalTime());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Quantity;
        yield return FromUtc;
        yield return ToUtc;
    }
}
