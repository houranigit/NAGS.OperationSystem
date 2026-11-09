namespace Operations.Contracts.Readers;

/// <summary>
/// Cross-module read seam used immediately before notification delivery to reject reminder events
/// whose flight snapshot is no longer current.
/// </summary>
public interface IFlightReminderEligibilityReader
{
    public Task<bool> IsEligibleAsync(
        Guid flightId,
        Guid staffMemberId,
        DateTimeOffset scheduledArrivalUtc,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken = default);
}
