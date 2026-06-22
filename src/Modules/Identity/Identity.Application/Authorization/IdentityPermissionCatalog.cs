using BuildingBlocks.Contracts.Authorization;
using Identity.Domain.Authorization;

namespace Identity.Application.Authorization;

/// <summary>
/// Contributes the Identity module's permissions to the composed registry. All Identity
/// administration permissions are System Administrator-only.
/// </summary>
public sealed class IdentityPermissionCatalog : IPermissionCatalog
{
    private static readonly IReadOnlyList<UserType> AdminOnly = [UserType.SystemAdministrator];

    public string Module => "identity";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
        IdentityPermissions.All.Select(p => new PermissionDescriptor(p, AdminOnly)).ToList();
}
