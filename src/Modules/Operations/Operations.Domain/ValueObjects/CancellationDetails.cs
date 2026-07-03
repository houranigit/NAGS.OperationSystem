using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Who cancelled the customer's flight, when, and an optional reason. Captured on a cancellation work order.</summary>
public sealed class CancellationDetails : ValueObject
{
    private CancellationDetails() { }

    public CancellationDetails(Guid canceledByUserId, DateTimeOffset canceledAtUtc, string? reason)
    {
        CanceledByUserId = canceledByUserId;
        CanceledAtUtc = canceledAtUtc;
        Reason = reason;
    }

    public Guid CanceledByUserId { get; private set; }
    public DateTimeOffset CanceledAtUtc { get; private set; }
    public string? Reason { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CanceledByUserId;
        yield return CanceledAtUtc;
        yield return Reason;
    }
}
