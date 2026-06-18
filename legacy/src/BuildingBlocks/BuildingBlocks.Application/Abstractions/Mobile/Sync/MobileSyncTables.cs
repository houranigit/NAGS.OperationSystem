namespace BuildingBlocks.Application.Abstractions.Mobile.Sync;

/// <summary>
/// Stable wire identifiers for every table the v2 mobile client caches locally.
/// One-to-one with the Room tables on the device — the mobile <c>RealtimeChannel</c>
/// uses <see cref="MobileSyncChange.Table"/> to route an incoming envelope to the
/// correct DAO, so the strings here are part of the public contract and must not be
/// renamed without a coordinated mobile release.
/// </summary>
public static class MobileSyncTables
{
    /// <summary>Non-AOG flights the calling employee is rostered on (Room: <c>flights_my</c>).</summary>
    public const string Flights = "flights";

    /// <summary>AOG flights at the calling employee's station (Room: <c>flights_aog</c>).</summary>
    public const string FlightsAog = "flights-aog";

    /// <summary>Ad Hoc operation-type flights at the calling employee's station (Room: <c>flights_ad_hoc</c>).</summary>
    public const string FlightsAdHoc = "flights-ad-hoc";

    /// <summary>Active employees at the calling employee's station.</summary>
    public const string Employees = "employees";

    /// <summary>Active services catalog (AOG seed excluded from the mobile view).</summary>
    public const string Services = "services";

    /// <summary>Active tools catalog.</summary>
    public const string Tools = "tools";

    /// <summary>Active materials catalog.</summary>
    public const string Materials = "materials";

    /// <summary>Active general-supports catalog.</summary>
    public const string GeneralSupports = "general-supports";

    /// <summary>Active customers catalog.</summary>
    public const string Customers = "customers";
}

/// <summary>
/// Stable wire identifiers for the <see cref="MobileSyncChange.Op"/> field. Mobile
/// uses these to pick the apply-side strategy (full-table refresh vs. single-row
/// upsert vs. single-row delete) so the strings here are part of the public contract.
/// </summary>
public static class MobileSyncOps
{
    /// <summary>Apply a single row insert / update. Mobile fetches the canonical row when <c>Payload</c> is null.</summary>
    public const string Upsert = "upsert";

    /// <summary>Apply a single row deletion. Mobile drops the row matching <c>EntityId</c>.</summary>
    public const string Delete = "delete";

    /// <summary>Force a full-table re-fetch through the existing sync coordinator (used for bulk catalog imports).</summary>
    public const string Refresh = "refresh";
}

/// <summary>
/// Audience prefixes for SignalR group routing. The broadcaster splits an envelope's
/// <see cref="MobileSyncChange.Audience"/> string on the first colon: the prefix
/// here picks the routing strategy, the suffix is the routing key (employee id or
/// station code).
/// </summary>
public static class MobileSyncAudience
{
    /// <summary>One specific employee — group name <c>employee:{guid}</c>.</summary>
    public const string EmployeePrefix = "employee:";

    /// <summary>All employees at one station — group name <c>station:{stationCode}</c>.</summary>
    public const string StationPrefix = "station:";

    /// <summary>Every connected mobile client, regardless of station. Used for catalog changes.</summary>
    public const string AllStations = "all-stations";
}
