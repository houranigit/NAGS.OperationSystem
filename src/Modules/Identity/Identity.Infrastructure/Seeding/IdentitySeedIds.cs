namespace Identity.Infrastructure.Seeding;

/// <summary>Stable, never-regenerated identifiers for seeded Identity data.</summary>
public static class IdentitySeedIds
{
    /// <summary>The system actor recorded for automated/seeded actions.</summary>
    public static readonly Guid SystemUserId = new("00000000-0000-0000-0000-000000000001");

    public const string SystemAdminRoleName = "System Administrator";
}
