namespace Identity.Domain.Authorization;

/// <summary>
/// The Identity module's permission catalog. Permission names use the lowercase
/// <c>module.resource.action</c> convention. These are the source of truth for what a role
/// may be granted within Identity.
/// </summary>
public static class IdentityPermissions
{
    public static class Users
    {
        public const string View = "identity.users.view";
        public const string Create = "identity.users.create";
        public const string Update = "identity.users.update";
        public const string Invite = "identity.users.invite";
        public const string Deactivate = "identity.users.deactivate";
        public const string Lock = "identity.users.lock";
        public const string Unlock = "identity.users.unlock";
        public const string AssignRole = "identity.users.assign-role";
    }

    public static class Roles
    {
        public const string View = "identity.roles.view";
        public const string Create = "identity.roles.create";
        public const string Update = "identity.roles.update";
        public const string Delete = "identity.roles.delete";
        public const string ManagePermissions = "identity.roles.manage-permissions";
    }

    public static class Sessions
    {
        public const string View = "identity.sessions.view";
        public const string Revoke = "identity.sessions.revoke";
    }

    /// <summary>Every permission defined by the Identity module.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Users.View, Users.Create, Users.Update, Users.Invite,
        Users.Deactivate, Users.Lock, Users.Unlock, Users.AssignRole,
        Roles.View, Roles.Create, Roles.Update, Roles.Delete, Roles.ManagePermissions,
        Sessions.View, Sessions.Revoke
    ];

    public static bool IsKnown(string permission) => All.Contains(permission);
}
