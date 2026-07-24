using BuildingBlocks.Contracts.Authorization;
using Identity.Domain.Authorization;

namespace Identity.Application.Authorization;

/// <summary>
/// Contributes the Identity module's permissions to the composed registry. Viewer Only roles may
/// read the Users and Roles pages plus supporting session data, but cannot mutate Identity state.
/// </summary>
public sealed class IdentityPermissionCatalog : IPermissionCatalog
{
    private static readonly IReadOnlyList<UserType> AdminOnly = [UserType.SystemAdministrator];
    private static readonly IReadOnlyList<UserType> AdminAndViewer =
        [UserType.SystemAdministrator, UserType.ViewerOnly];

    public string Module => "identity";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
    [
        new(IdentityPermissions.Users.View, AdminAndViewer, GrantsPortalPage: true),
        new(IdentityPermissions.Users.Update, AdminOnly),
        new(IdentityPermissions.Users.Invite, AdminOnly),
        new(IdentityPermissions.Users.Deactivate, AdminOnly),
        new(IdentityPermissions.Users.Lock, AdminOnly),
        new(IdentityPermissions.Users.Unlock, AdminOnly),
        new(IdentityPermissions.Users.AssignRole, AdminOnly),
        new(IdentityPermissions.Users.Suspend, AdminOnly),
        new(IdentityPermissions.Users.RestoreAccess, AdminOnly),
        new(IdentityPermissions.Users.ResetMfa, AdminOnly),

        new(IdentityPermissions.Roles.View, AdminAndViewer, GrantsPortalPage: true),
        new(IdentityPermissions.Roles.Create, AdminOnly),
        new(IdentityPermissions.Roles.Update, AdminOnly),
        new(IdentityPermissions.Roles.Delete, AdminOnly),
        new(IdentityPermissions.Roles.ManagePermissions, AdminOnly),

        new(IdentityPermissions.Sessions.View, AdminAndViewer),
        new(IdentityPermissions.Sessions.Revoke, AdminOnly)
    ];
}
