namespace OperationsSystem.Blazor.Client.Localization;

/// <summary>
/// Centralized user-facing strings. Per the frontend rules, UI literals must not be scattered
/// across components; they live here until a full resource-based localization layer is wired.
/// </summary>
public static class UiStrings
{
    public static class App
    {
        public const string Name = "Operations System";
        public const string Portal = "Operations Portal";
        public const string Version = "v1.0.0";
        public const string BlazorPortal = "Blazor portal";
    }

    public static class Common
    {
        public const string Loading = "Loading...";
        public const string Save = "Save";
        public const string Cancel = "Cancel";
        public const string Create = "Create";
        public const string Delete = "Delete";
        public const string Edit = "Edit";
        public const string Close = "Close";
        public const string Confirm = "Confirm";
        public const string Search = "Search";
        public const string Actions = "Actions";
        public const string Open = "Open";
        public const string Manage = "Manage";
        public const string Back = "Back";
        public const string Yes = "Yes";
        public const string No = "No";
        public const string NoData = "No data to display.";
        public const string ItemsPerPage = "Rows per page";
        public const string SomethingWentWrong = "Something went wrong. Please try again.";
        public const string SignOut = "Sign out";
        public const string Refresh = "Refresh";
        public const string Details = "Details";
        public const string Overview = "Overview";
    }

    public static class Auth
    {
        public const string SignInTitle = "Sign in";
        public const string SignInSubtitle = "Use your portal account to continue.";
        public const string Email = "Email";
        public const string Password = "Password";
        public const string SignInButton = "Sign in";
        public const string CredentialsRequired = "Email and password are required.";
        public const string InvalidCredentials = "The email or password is incorrect.";

        public const string ActivateTitle = "Activate your account";
        public const string ActivateSubtitle = "Set a password to finish setting up your account.";
        public const string ActivationToken = "Activation code";
        public const string NewPassword = "New password";
        public const string ConfirmPassword = "Confirm password";
        public const string ActivateButton = "Activate account";
        public const string ActivationSuccess = "Your account is active. You can sign in now.";
        public const string PasswordsDoNotMatch = "The passwords do not match.";
        public const string PasswordTooShort = "Use at least 8 characters.";
        public const string GoToSignIn = "Go to sign in";

        public const string BrandHeadline = "Operations, under control.";
        public const string BrandSubtext = "Manage your people, roles, and access from one secure, bilingual portal.";
        public const string BrandPointAccess = "Granular role-based access control";
        public const string BrandPointSessions = "Live session and device management";
        public const string BrandPointBilingual = "Full Arabic and English support";
        public const string Copyright = "© 2026 Operations System. All rights reserved.";
    }

    public static class Nav
    {
        public const string Dashboard = "Dashboard";
        public const string Users = "Users";
        public const string Roles = "Roles";
        public const string Account = "Account";
        public const string Overview = "Overview";
        public const string Administration = "Administration";
        public const string ToggleMenu = "Toggle navigation";
        public const string Language = "Language";
    }

    public static class Dashboard
    {
        public const string WelcomeBack = "Welcome back";
        public const string Overview = "Overview";
        public const string IdentityCard = "Account access";
        public const string PermissionsCard = "Permissions";
        public const string SessionsCard = "Active sessions";
        public const string ManageUsers = "Manage users";
        public const string ManageRoles = "Manage roles";
    }

    public static class Account
    {
        public const string Title = "Account";
        public const string Profile = "Profile";
        public const string Role = "Role";
        public const string Permissions = "Permissions";
        public const string DisplayName = "Display name";
        public const string ProfileUpdated = "Your profile has been updated.";

        public const string ChangePassword = "Change password";
        public const string CurrentPassword = "Current password";
        public const string PasswordChanged = "Your password has been changed. Please sign in again.";

        public const string Sessions = "Sessions";
        public const string SessionsDescription = "Devices currently signed in with your account.";
        public const string ThisDevice = "This device";
        public const string SignOutOthers = "Sign out other sessions";
        public const string RevokeSession = "Revoke";
    }

    public static class Users
    {
        public const string Title = "Users";
        public const string Description = "Invite, edit, and manage portal accounts.";
        public const string Invite = "Invite user";
        public const string Empty = "No users match your filters.";
        public const string CountLabel = "users";
        public const string SearchPlaceholder = "Search by name or email";
        public const string DisplayName = "Name";
        public const string Email = "Email";
        public const string Status = "Status";
        public const string Role = "Role";
        public const string LastLogin = "Last login";
        public const string Created = "Created";
        public const string AllStatuses = "All statuses";
        public const string AllRoles = "All roles";
        public const string FilterByStatus = "All statuses";
        public const string FilterByRole = "All roles";
        public const string Lock = "Lock";
        public const string Unlock = "Unlock";
        public const string Deactivate = "Deactivate";
        public const string ResendInvitation = "Resend invitation";
        public const string AssignRole = "Assign role";
        public const string LockedOut = "Locked out";
        public const string Never = "Never";

        public const string InviteTitle = "Invite a user";
        public const string InviteSuccess = "Invitation sent.";
        public const string InvitationTokenLabel = "Invitation code (development)";
        public const string EditTitle = "Edit user";
        public const string AssignRoleTitle = "Assign role";
        public const string ConfirmDeactivate = "Deactivate this user? They will be signed out of all sessions.";
        public const string ConfirmLock = "Lock this user out of signing in?";
    }

    public static class Roles
    {
        public const string Title = "Roles";
        public const string Description = "Define roles and the permissions they grant.";
        public const string Create = "New role";
        public const string Empty = "No roles match your search.";
        public const string CountLabel = "roles";
        public const string SearchPlaceholder = "Search roles";
        public const string Name = "Name";
        public const string RoleDescription = "Description";
        public const string System = "System";
        public const string PermissionCount = "Permissions";
        public const string UserCount = "Users";
        public const string NoDescription = "No description";
        public const string CreateTitle = "Create role";
        public const string EditTitle = "Edit role";
        public const string Permissions = "Permissions";
        public const string PermissionsDescription = "Select the permissions this role grants.";
        public const string PermissionsSaved = "Permissions updated.";
        public const string SystemRoleLocked = "System roles cannot be edited or deleted.";
        public const string ConfirmDelete = "Delete this role? Users must not be assigned to it.";
        public const string Created = "Role created.";
        public const string Updated = "Role updated.";
        public const string Deleted = "Role deleted.";
    }

    public static class Sessions
    {
        public const string Title = "Sessions";
        public const string Created = "Signed in";
        public const string Expires = "Expires";
        public const string Device = "Device";
        public const string IpAddress = "IP address";
        public const string State = "State";
        public const string Active = "Active";
        public const string Revoked = "Revoked";
        public const string RevokeAll = "Revoke all sessions";
        public const string Empty = "No sessions found.";
    }

    public static class Errors
    {
        public const string Forbidden = "You do not have permission to view this.";
        public const string NotFound = "The requested item was not found.";
        public const string LoadFailed = "We couldn't load this. Please try again.";
    }
}
