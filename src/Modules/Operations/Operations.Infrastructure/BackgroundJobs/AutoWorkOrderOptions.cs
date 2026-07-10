namespace Operations.Infrastructure.BackgroundJobs;

public sealed class AutoWorkOrderOptions
{
    public const string SectionName = "Operations:AutoWorkOrder";

    public bool Enabled { get; set; } = true;
    public int DelayMinutes { get; set; } = 60;
    public int PollIntervalSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 25;
}
