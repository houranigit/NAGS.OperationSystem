namespace Operations.Infrastructure.BackgroundJobs;

/// <summary>Durable state for one employee-facing reminder milestone.</summary>
public sealed class FlightReminderSchedule
{
    private FlightReminderSchedule() { }

    public Guid Id { get; private set; }
    public Guid FlightId { get; private set; }
    public Guid StaffMemberId { get; private set; }
    public int LeadTimeMinutes { get; private set; }
    public DateTimeOffset ScheduledArrivalUtc { get; private set; }
    public DateTimeOffset DueAtUtc { get; private set; }
    public FlightReminderState State { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }
    public DateTimeOffset? SkippedAtUtc { get; private set; }
    public string? SkipReason { get; private set; }

    public static FlightReminderSchedule Create(
        Guid flightId,
        Guid staffMemberId,
        DateTimeOffset scheduledArrivalUtc,
        int leadTimeMinutes,
        DateTimeOffset now,
        TimeSpan enrollmentLatenessTolerance)
    {
        if (flightId == Guid.Empty)
            throw new ArgumentException("Flight id is required.", nameof(flightId));
        if (staffMemberId == Guid.Empty)
            throw new ArgumentException("Staff member id is required.", nameof(staffMemberId));
        if (!FlightReminderLeadTimes.All.Contains(leadTimeMinutes))
            throw new ArgumentOutOfRangeException(nameof(leadTimeMinutes));
        if (enrollmentLatenessTolerance < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(enrollmentLatenessTolerance));

        scheduledArrivalUtc = scheduledArrivalUtc.ToUniversalTime();
        now = now.ToUniversalTime();
        var dueAtUtc = scheduledArrivalUtc.AddMinutes(-leadTimeMinutes);
        var isAlreadyStale = dueAtUtc < now.Subtract(enrollmentLatenessTolerance);

        return new FlightReminderSchedule
        {
            Id = Guid.NewGuid(),
            FlightId = flightId,
            StaffMemberId = staffMemberId,
            LeadTimeMinutes = leadTimeMinutes,
            ScheduledArrivalUtc = scheduledArrivalUtc,
            DueAtUtc = dueAtUtc,
            State = isAlreadyStale ? FlightReminderState.Skipped : FlightReminderState.Pending,
            CreatedAtUtc = now,
            SkippedAtUtc = isAlreadyStale ? now : null,
            SkipReason = isAlreadyStale ? "Milestone had passed before reminder enrollment." : null
        };
    }

    public void MarkDispatched(DateTimeOffset now)
    {
        if (State != FlightReminderState.Pending)
            return;

        State = FlightReminderState.Dispatched;
        DispatchedAtUtc = now;
    }

    public void MarkSkipped(DateTimeOffset now, string reason)
    {
        if (State != FlightReminderState.Pending)
            return;

        State = FlightReminderState.Skipped;
        SkippedAtUtc = now;
        SkipReason = reason;
    }
}

public enum FlightReminderState
{
    Pending = 0,
    Dispatched = 1,
    Skipped = 2
}

public static class FlightReminderLeadTimes
{
    public const int TwelveHours = 12 * 60;
    public const int TwoHours = 2 * 60;
    public const int ThirtyMinutes = 30;

    public static readonly IReadOnlySet<int> All =
        new HashSet<int> { TwelveHours, TwoHours, ThirtyMinutes };

    public static readonly IReadOnlyList<int> Descending =
        [TwelveHours, TwoHours, ThirtyMinutes];

    public const int Count = 3;

    public static int? CurrentFor(DateTimeOffset scheduledArrivalUtc, DateTimeOffset now)
    {
        var remaining = scheduledArrivalUtc - now;
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromHours(12))
            return null;
        if (remaining <= TimeSpan.FromMinutes(30))
            return ThirtyMinutes;
        return remaining <= TimeSpan.FromHours(2) ? TwoHours : TwelveHours;
    }
}
