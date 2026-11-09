namespace Operations.Infrastructure.BackgroundJobs;

public sealed class FlightReminderOptions
{
    public const string SectionName = "Operations:FlightReminders";
    public const int MinimumPollIntervalSeconds = 15;
    public const int MaximumPollIntervalSeconds = 3600;
    public const int MinimumEnrollmentLookaheadHours = 13;
    public const int MaximumEnrollmentLookaheadHours = 168;
    public const int MaximumEnrollmentLatenessToleranceMinutes = 60;
    public const int MaximumBatchSize = 2000;
    public const int MinimumTerminalRetentionDays = 1;
    public const int MaximumTerminalRetentionDays = 3650;

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int EnrollmentLookaheadHours { get; set; } = 13;
    public int EnrollmentLatenessToleranceMinutes { get; set; } = 5;
    public int EnrollmentBatchSize { get; set; } = 500;
    public int DispatchBatchSize { get; set; } = 500;
    public int TerminalRetentionDays { get; set; } = 30;
    public int CleanupBatchSize { get; set; } = 500;
}
