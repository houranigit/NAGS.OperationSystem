namespace MasterData.Contracts.Seeding;

/// <summary>Stable identifiers for system-seeded MasterData rows that other modules may reference.</summary>
public static class WellKnownMasterDataIds
{
    public static readonly Guid AdHocOperationType = new("30000000-0000-0000-0000-000000000001");

    public static readonly Guid AircraftPerLandingService = new("40000000-0000-0000-0000-000000000001");

    public static readonly Guid UnknownCustomer = new("50000000-0000-0000-0000-000000000001");
}
