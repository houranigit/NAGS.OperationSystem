namespace BuildingBlocks.Contracts.Authorization;

/// <summary>
/// The fixed business identity/data-scope of an account. This is an authorization-framework
/// concept shared across modules: a permission declares which user types it is compatible with,
/// and MasterData requests portal access for one of these types. It is deliberately small and
/// stable; it is not a role (roles are configurable permission collections).
/// </summary>
public enum UserType
{
    /// <summary>Internal administrator with system-wide access. No MasterData link.</summary>
    SystemAdministrator = 0,

    /// <summary>A portal account for a person working for a station, scoped to that station.</summary>
    StationStaff = 1,

    /// <summary>A portal account for a customer's contact, scoped to that customer.</summary>
    CustomerContact = 2
}
