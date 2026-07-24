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
    CustomerContact = 2,

    /// <summary>A direct, unlinked portal account with globally scoped read-only access.</summary>
    ViewerOnly = 3
}

/// <summary>Provisioning and data-link traits for the fixed account types.</summary>
public static class UserTypeExtensions
{
    /// <summary>True when an account can be invited directly without a MasterData record.</summary>
    public static bool IsDirectlyProvisioned(this UserType userType) =>
        userType is UserType.SystemAdministrator or UserType.ViewerOnly;

    /// <summary>True when an account must originate from and remain linked to MasterData.</summary>
    public static bool RequiresExternalReference(this UserType userType) =>
        userType is UserType.StationStaff or UserType.CustomerContact;
}
