using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>The cancellation time and remarks supplied in an employee's submission.</summary>
public sealed class CancellationDetails : ValueObject
{
    private CancellationDetails() { }

    public DateTimeOffset CanceledAtUtc { get; private set; }
    public string? Remarks { get; private set; }

    public static Result<CancellationDetails> Create(DateTimeOffset canceledAt, string? remarks)
    {
        if (canceledAt == default)
            return Error.Validation("Cancellation time is required.", "Operations.CancellationDetails.TimeRequired");

        return new CancellationDetails
        {
            CanceledAtUtc = canceledAt.ToUniversalTime(),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim()
        };
    }

    internal CancellationDetails Copy() => Create(CanceledAtUtc, Remarks).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CanceledAtUtc;
        yield return Remarks;
    }
}
