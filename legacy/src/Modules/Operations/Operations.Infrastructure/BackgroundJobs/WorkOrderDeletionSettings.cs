namespace Operations.Infrastructure.BackgroundJobs;

/// <summary>
/// Platform setting bound from <c>Operations:WorkOrderDeletion</c> in appsettings. Controls
/// how long a sibling work order may remain in the <c>Deleting</c> state before the
/// background job removes it. Bumping this value in production gives operators a longer
/// grace window to revoke an approval and recover the rejected siblings.
/// </summary>
public sealed class WorkOrderDeletionSettings
{
    public const string SectionName = "Operations:WorkOrderDeletion";

    /// <summary>Minutes a work order stays in <c>Deleting</c> before it is hard-deleted. Default 15.</summary>
    public int DelayMinutes { get; set; } = 15;

    /// <summary>How often the deletion job polls the database (seconds). Default 30.</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Maximum rows the job processes in a single tick. Default 50.</summary>
    public int BatchSize { get; set; } = 50;
}
