namespace Operations.Domain.Flights;

/// <summary>The latest committed work interaction by one account with one flight.</summary>
public sealed class FlightUserActivity
{
    private FlightUserActivity() { }

    public Guid UserId { get; private set; }
    public Guid FlightId { get; private set; }
    public DateTimeOffset LastInteractionAtUtc { get; private set; }
    public bool UsedSearch { get; private set; }

    public static FlightUserActivity Create(Guid userId, Guid flightId, DateTimeOffset occurredAtUtc, bool usedSearch = false)
    {
        if (userId == Guid.Empty) throw new ArgumentException("An account is required.", nameof(userId));
        if (flightId == Guid.Empty) throw new ArgumentException("A flight is required.", nameof(flightId));
        if (occurredAtUtc == default) throw new ArgumentException("An interaction time is required.", nameof(occurredAtUtc));
        return new FlightUserActivity
        {
            UserId = userId, FlightId = flightId,
            LastInteractionAtUtc = occurredAtUtc.ToUniversalTime(), UsedSearch = usedSearch
        };
    }

    public void Touch(DateTimeOffset occurredAtUtc, bool usedSearch)
    {
        if (occurredAtUtc == default) throw new ArgumentException("An interaction time is required.", nameof(occurredAtUtc));
        if (occurredAtUtc > LastInteractionAtUtc) LastInteractionAtUtc = occurredAtUtc.ToUniversalTime();
        UsedSearch |= usedSearch;
    }
}
