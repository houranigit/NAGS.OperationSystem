using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

public sealed class CancellationDetails : ValueObject
{
    private CancellationDetails() { }

    private CancellationDetails(DateTimeOffset canceledAtUtc, string? reason)
    {
        CanceledAtUtc = canceledAtUtc;
        Reason = reason;
    }

    public DateTimeOffset CanceledAtUtc { get; private set; }
    public string? Reason { get; private set; }

    public static Result<CancellationDetails> Create(DateTimeOffset canceledAtUtc, string? reason)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason?.Length > 1000)
            return Error.Validation("Cancellation reason must be at most 1000 characters.", "Operations.Cancellation.ReasonTooLong");

        return new CancellationDetails(canceledAtUtc.ToUniversalTime(), normalizedReason);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CanceledAtUtc;
        yield return Reason;
    }
}
