using BuildingBlocks.Contracts.Authorization;

namespace BuildingBlocks.Application.Abstractions;

/// <summary>
/// The composed, cross-module permission catalog. Built from every registered
/// <see cref="IPermissionCatalog"/> so role validation knows the full set of grantable permissions
/// and their user-type compatibility, not just one module's.
/// </summary>
public interface IPermissionRegistry
{
    public IReadOnlyList<PermissionDescriptor> All { get; }

    public bool IsKnown(string permission);

    /// <summary>True when the permission exists and may be granted to a role of the given user type.</summary>
    public bool IsCompatibleWith(string permission, UserType userType);

    /// <summary>The permission codes a role of the given user type may select from.</summary>
    public IReadOnlyList<string> CompatiblePermissions(UserType userType);
}
