namespace BuildingBlocks.Contracts.Authorization;

/// <summary>
/// One grantable permission plus the user types whose roles may contain it. Compatibility is a
/// maximum set: it does not grant the permission automatically, it bounds what a role for a given
/// user type may select.
/// </summary>
public sealed record PermissionDescriptor(string Code, IReadOnlyList<UserType> CompatibleUserTypes)
{
    public bool IsCompatibleWith(UserType userType) => CompatibleUserTypes.Contains(userType);
}

/// <summary>
/// Implemented by each module to contribute its permissions and their user-type compatibility.
/// Identity composes all registered catalogs to validate role permissions across modules, so the
/// Identity-only catalog is no longer sufficient once other modules add permissions.
/// </summary>
public interface IPermissionCatalog
{
    public string Module { get; }

    public IReadOnlyList<PermissionDescriptor> Permissions { get; }
}
