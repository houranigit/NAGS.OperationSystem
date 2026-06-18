using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Captured at the moment a contract is manually terminated. Owned by the Contract aggregate
/// as a nullable VO — null until <see cref="Contract.Terminate"/> is called.
/// </summary>
public sealed class Termination : ValueObject
{
    public string Reason { get; private set; } = null!;
    public DateTime AtUtc { get; private set; }
    public Guid ByUserId { get; private set; }

    private Termination() { }

    private Termination(string reason, DateTime atUtc, Guid byUserId)
    {
        Reason = reason;
        AtUtc = atUtc;
        ByUserId = byUserId;
    }

    public static Result<Termination> Create(string? reason, DateTime atUtc, Guid byUserId)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Error.Validation("Termination reason is required.");
        if (reason.Length > 500)
            return Error.Validation("Termination reason must not exceed 500 characters.");
        if (byUserId == Guid.Empty)
            return Error.Validation("Termination requires a current user.");

        return new Termination(reason.Trim(), atUtc, byUserId);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Reason;
        yield return AtUtc;
        yield return ByUserId;
    }
}
