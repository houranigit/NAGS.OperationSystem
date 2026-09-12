namespace MasterData.Contracts.Seeding;

/// <summary>Stable identifiers for system-seeded MasterData rows that other modules may reference.</summary>
public static class WellKnownMasterDataIds
{
    public static readonly Guid AdHocOperationType = new("30000000-0000-0000-0000-000000000001");

    public static readonly Guid AircraftPerLandingService = new("40000000-0000-0000-0000-000000000001");

    public static readonly Guid UnknownCustomer = new("50000000-0000-0000-0000-000000000001");

    public static readonly Guid UnknownService = new("40000000-0000-0000-0000-000000000003");
    public static readonly Guid UnknownTool = new("60000000-0000-0000-0000-000000000001");
    public static readonly Guid UnknownMaterial = new("70000000-0000-0000-0000-000000000001");
    public static readonly Guid UnknownGeneralSupport = new("80000000-0000-0000-0000-000000000001");
}
