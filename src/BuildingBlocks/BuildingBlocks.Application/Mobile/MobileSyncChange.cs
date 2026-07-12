namespace BuildingBlocks.Application.Mobile;

/// <summary>
/// Wire envelope for one mobile-sync change pushed over SignalR (or returned by the
/// REST catch-up endpoint after a reconnect). One shape, used by every push and the
/// catch-up endpoint alike, so the mobile client has a single apply path.
/// </summary>
/// <param name="Table">Logical table identifier — see <see cref="MobileSyncTables"/>.</param>
/// <param name="Op">Operation kind — see <see cref="MobileSyncOps"/>.</param>
/// <param name="EntityId">
/// Server id of the affected row when <see cref="Op"/> is <c>upsert</c> or <c>delete</c>;
/// null when <see cref="Op"/> is <c>refresh</c>.
/// </param>
/// <param name="Audience">
/// SignalR routing key — see <see cref="MobileSyncAudience"/>. Used server-side to pick
/// the broadcast group; the mobile client receives it for diagnostics only.
/// </param>
/// <param name="Version">
/// Per-table monotonic cursor: the affected row's <c>UpdatedAtUtc</c> (or "now" for
/// refresh envelopes) serialised as ISO-8601 UTC. The client stores the last applied
/// cursor per table and presents it on reconnect catch-up.
/// </param>
/// <param name="Payload">
/// Inline row JSON when the row is small enough to wire-embed. Null for flight rows:
/// the client re-fetches by id through the mobile flight read endpoint so we keep a
/// single projection path.
/// </param>
/// <param name="OriginMutationId">
/// When the broadcast was caused by a mobile-originated mutation, carries that
/// mutation's <c>clientMutationId</c> so the originating device can reconcile its
/// outbox row on echo.
/// </param>
public sealed record MobileSyncChange(
    string Table,
    string Op,
    string? EntityId,
    string Audience,
    DateTimeOffset Version,
    string? Payload = null,
    string? OriginMutationId = null);

/// <summary>
/// Stable wire identifiers for every table the mobile client caches locally. These map
/// one-to-one to Room tables on the device and are part of the public contract — do not
/// rename without a coordinated mobile release.
/// </summary>
public static class MobileSyncTables
{
    /// <summary>Non-Per-Landing flights the calling staff member is rostered on (Room: <c>flights_my</c>).</summary>
    public const string Flights = "flights";

    /// <summary>Per-Landing flights at the calling staff member's station (Room: <c>flights_per_landing</c>).</summary>
    public const string FlightsPerLanding = "flights-per-landing";

    /// <summary>Ad Hoc operation-type flights at the calling staff member's station (Room: <c>flights_ad_hoc</c>).</summary>
    public const string FlightsAdHoc = "flights-ad-hoc";

    /// <summary>Active staff members at the calling staff member's station.</summary>
    public const string Employees = "employees";

    /// <summary>Active services catalog.</summary>
    public const string Services = "services";

    /// <summary>Active tools catalog.</summary>
    public const string Tools = "tools";

    /// <summary>Active materials catalog.</summary>
    public const string Materials = "materials";

    /// <summary>Active general-supports catalog.</summary>
    public const string GeneralSupports = "general-supports";

    /// <summary>Active customers catalog.</summary>
    public const string Customers = "customers";

    /// <summary>Active aircraft-types catalog.</summary>
    public const string AircraftTypes = "aircraft-types";

    /// <summary>All logical tables, in the order the catch-up endpoint returns them when unspecified.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Flights, FlightsPerLanding, FlightsAdHoc, Employees,
        Services, Tools, Materials, GeneralSupports, Customers, AircraftTypes
    ];
}

/// <summary>
/// Stable wire identifiers for <see cref="MobileSyncChange.Op"/>. The client picks the
/// apply strategy from these (full-table refresh vs single-row upsert vs single-row delete).
/// </summary>
public static class MobileSyncOps
{
    /// <summary>Apply a single-row insert/update. The client fetches the canonical row when <c>Payload</c> is null.</summary>
    public const string Upsert = "upsert";

    /// <summary>Apply a single-row deletion. The client drops the row matching <c>EntityId</c>.</summary>
    public const string Delete = "delete";

    /// <summary>Force a full-table re-fetch through the client's sync coordinator.</summary>
    public const string Refresh = "refresh";
}

/// <summary>
/// Audience keys for SignalR group routing. Prefixed audiences map to one SignalR group;
/// <see cref="AllStations"/> fans out to every connected client.
/// </summary>
public static class MobileSyncAudience
{
    /// <summary>One specific staff member — group name <c>employee:{staffMemberId}</c>.</summary>
    public const string EmployeePrefix = "employee:";

    /// <summary>All staff at one station — group name <c>station:{stationIata}</c>.</summary>
    public const string StationPrefix = "station:";

    /// <summary>Every connected mobile client. Used for catalog changes.</summary>
    public const string AllStations = "all-stations";

    public static string Employee(Guid staffMemberId) => $"{EmployeePrefix}{staffMemberId}";

    public static string Station(string stationIata) => $"{StationPrefix}{stationIata}";
}
