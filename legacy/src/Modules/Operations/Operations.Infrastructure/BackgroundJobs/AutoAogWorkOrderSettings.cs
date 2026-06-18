namespace Operations.Infrastructure.BackgroundJobs;

/// <summary>
/// Platform setting bound from <c>Operations:AutoAogWorkOrder</c> in appsettings. Drives the
/// background job that auto-issues a basic work order for an AOG flight when its scheduled
/// departure has elapsed by <see cref="DelayMinutes"/> and no employee has acted on it
/// (the flight is still <c>Scheduled</c> — no work order attached, so no transition to
/// <c>InProgress</c>).
/// </summary>
/// <remarks>
/// The auto-issued work order copies the flight number, aircraft type and full schedule
/// from the flight; ATA/ATD are seeded with the flight's STA/STD; service lines and tasks
/// are left empty. Attaching it flips the flight to <c>InProgress</c> via
/// <see cref="Operations.Domain.Aggregates.Flight.Flight.AttachWorkOrder"/>.
/// </remarks>
public sealed class AutoAogWorkOrderSettings
{
    public const string SectionName = "Operations:AutoAogWorkOrder";

    /// <summary>Master switch. When <c>false</c> the job loads but performs no work. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minutes that must elapse past the flight's STD before the job auto-issues a work
    /// order. The flight must still be <c>Scheduled</c> (no employee acted on it). Default 60.
    /// </summary>
    public int DelayMinutes { get; set; } = 60;

    /// <summary>How often the job polls the database (seconds). Default 60.</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>Maximum flights the job auto-issues in a single tick. Default 25.</summary>
    public int BatchSize { get; set; } = 25;
}
