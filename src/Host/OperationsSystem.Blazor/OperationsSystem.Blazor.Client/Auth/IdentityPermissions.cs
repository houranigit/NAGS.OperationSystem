namespace OperationsSystem.Blazor.Client.Auth;

/// <summary>
/// Client-side mirror of the Identity module's permission names (the backend remains authoritative).
/// Used only to gate UI; the API enforces these on every request.
/// </summary>
public static class IdentityPermissions
{
    public const string UsersView = "identity.users.view";
    public const string UsersUpdate = "identity.users.update";
    public const string UsersInvite = "identity.users.invite";
    public const string UsersDeactivate = "identity.users.deactivate";
    public const string UsersLock = "identity.users.lock";
    public const string UsersUnlock = "identity.users.unlock";
    public const string UsersAssignRole = "identity.users.assign-role";
    public const string UsersSuspend = "identity.users.suspend";
    public const string UsersRestoreAccess = "identity.users.restore-access";
    public const string UsersResetMfa = "identity.users.reset-mfa";

    public const string RolesView = "identity.roles.view";
    public const string RolesCreate = "identity.roles.create";
    public const string RolesUpdate = "identity.roles.update";
    public const string RolesDelete = "identity.roles.delete";
    public const string RolesManagePermissions = "identity.roles.manage-permissions";

    public const string SessionsView = "identity.sessions.view";
    public const string SessionsRevoke = "identity.sessions.revoke";
}
