using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

public enum CancellationType
{
    Unplanned = 0,
    Planned = 1
}

/// <summary>The cancellation time, type, and remarks supplied in a submission.</summary>
public sealed class CancellationDetails : ValueObject
{
    private CancellationDetails() { }

    public DateTimeOffset CanceledAtUtc { get; private set; }
    public string? Remarks { get; private set; }
    public CancellationType Type { get; private set; }

    public static Result<CancellationDetails> Create(DateTimeOffset canceledAt, string? remarks,
        CancellationType type = CancellationType.Unplanned)
    {
        if (canceledAt == default)
            return Error.Validation("Cancellation time is required.", "Operations.CancellationDetails.TimeRequired");
        if (!Enum.IsDefined(type))
            return Error.Validation("Cancellation type is invalid.", "Operations.CancellationDetails.TypeInvalid");

        var normalizedRemarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        if (type == CancellationType.Planned && normalizedRemarks is null)
            return Error.Validation("Remarks are required for a planned cancellation.", "Operations.CancellationDetails.RemarksRequired");

        return new CancellationDetails
        {
            CanceledAtUtc = canceledAt.ToUniversalTime(),
            Remarks = normalizedRemarks,
            Type = type
        };
    }

    internal CancellationDetails Copy() => Create(CanceledAtUtc, Remarks, Type).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CanceledAtUtc;
        yield return Remarks;
        yield return Type;
    }
}
