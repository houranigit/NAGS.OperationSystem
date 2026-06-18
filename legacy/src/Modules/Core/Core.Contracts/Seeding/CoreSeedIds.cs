namespace Core.Contracts.Seeding;

/// <summary>
/// Well-known fixed GUIDs for entities that are always seeded into the system.
/// These IDs are stable across all environments — never regenerate them.
/// Lives in Core.Contracts so other modules (e.g. Contracts) can reference well-known seed
/// identifiers without taking a dependency on Core.Application.
/// </summary>
public static class CoreSeedIds
{
    public static readonly Guid SarCurrency = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid UsdCurrency = new("10000000-0000-0000-0000-000000000002");

    public static readonly Guid SaudiArabia = new("20000000-0000-0000-0000-000000000001");

    public static readonly Guid AdHocOperationType = new("30000000-0000-0000-0000-000000000001");

    public static readonly Guid AogService = new("40000000-0000-0000-0000-000000000001");
    public static readonly Guid OnCallService = new("40000000-0000-0000-0000-000000000002");

    /// <summary>Used as CreatedById on seeded exchange rates and similar system actions.</summary>
    public static readonly Guid SystemUserId = new("00000000-0000-0000-0000-000000000001");
}
