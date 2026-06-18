namespace Identity.Domain.Authorization;

public static class Permissions
{
    public static class Portal
    {
        public const string Manage = "portal.manage";
    }

    public static class Scheduler
    {
        public const string Read        = "scheduler.read";
        public const string ReadLookups = "scheduler.lookups.read";
    }

    public static class Flights
    {
        public const string Create = "flights.create";
        public const string Read   = "flights.read";
        public const string Update = "flights.update";
    }

    public static class Users
    {
        public const string Create      = "users.create";
        public const string Read        = "users.read";
        public const string Update      = "users.update";
        public const string Delete      = "users.delete";
        public const string Lock        = "users.lock";
        public const string Unlock      = "users.unlock";
        public const string AssignRoles = "users.assign-roles";
        public const string Invite      = "users.invite";
    }

    public static class Roles
    {
        public const string Create            = "roles.create";
        public const string Read              = "roles.read";
        public const string Update            = "roles.update";
        public const string Delete            = "roles.delete";
        public const string ManagePermissions = "roles.manage-permissions";
    }

    public static class Sessions
    {
        public const string Read   = "sessions.read";
        public const string Revoke = "sessions.revoke";
    }
}
