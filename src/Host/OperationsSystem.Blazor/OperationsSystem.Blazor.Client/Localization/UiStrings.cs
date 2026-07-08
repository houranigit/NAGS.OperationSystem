namespace OperationsSystem.Blazor.Client.Localization;

/// <summary>
/// Centralized user-facing strings resolved from embedded EN/AR .resx resources.
/// </summary>
public static class UiStrings
{
    public static class App
    {
        public static string Name => UiText.Get("App.Name", "Operations System");
        public static string Portal => UiText.Get("App.Portal", "");
        public static string Version => UiText.Get("App.Version", "v1.0.0");
    }

    public static class Common
    {
        public static string Loading => UiText.Get("Common.Loading", "Loading...");
        public static string Save => UiText.Get("Common.Save", "Save");
        public static string Cancel => UiText.Get("Common.Cancel", "Cancel");
        public static string Create => UiText.Get("Common.Create", "Create");
        public static string Delete => UiText.Get("Common.Delete", "Delete");
        public static string Edit => UiText.Get("Common.Edit", "Edit");
        public static string Close => UiText.Get("Common.Close", "Close");
        public static string Confirm => UiText.Get("Common.Confirm", "Confirm");
        public static string Search => UiText.Get("Common.Search", "Search");
        public static string Actions => UiText.Get("Common.Actions", "Actions");
        public static string MoreActions => UiText.Get("Common.MoreActions", "More actions");
        public static string Open => UiText.Get("Common.Open", "Open");
        public static string Manage => UiText.Get("Common.Manage", "Manage");
        public static string Back => UiText.Get("Common.Back", "Back");
        public static string Yes => UiText.Get("Common.Yes", "Yes");
        public static string No => UiText.Get("Common.No", "No");
        public static string NoData => UiText.Get("Common.NoData", "No data to display.");
        public static string ItemsPerPage => UiText.Get("Common.ItemsPerPage", "Rows per page");
        public static string SomethingWentWrong => UiText.Get("Common.SomethingWentWrong", "Something went wrong. Please try again.");
        public static string SignOut => UiText.Get("Common.SignOut", "Sign out");
        public static string Refresh => UiText.Get("Common.Refresh", "Refresh");
        public static string Details => UiText.Get("Common.Details", "Details");
        public static string Overview => UiText.Get("Common.Overview", "Overview");
        public static string Name => UiText.Get("Common.Name", "Name");
        public static string ViewAll => UiText.Get("Common.ViewAll", "View all");
        public static string Copy => UiText.Get("Common.Copy", "Copy");
        public static string Copied => UiText.Get("Common.Copied", "Copied to clipboard");
        public static string JustNow => UiText.Get("Common.JustNow", "Just now");
        public static string MinutesAgo => UiText.Get("Common.MinutesAgo", "{0}m ago");
        public static string HoursAgo => UiText.Get("Common.HoursAgo", "{0}h ago");
        public static string DaysAgo => UiText.Get("Common.DaysAgo", "{0}d ago");
        public static string None => UiText.Get("Common.None", "-");
    }

    public static class Auth
    {
        public static string SignInTitle => UiText.Get("Auth.SignInTitle", "Sign in");
        public static string SignInSubtitle => UiText.Get("Auth.SignInSubtitle", "Use your portal account to continue.");
        public static string Email => UiText.Get("Auth.Email", "Email");
        public static string Password => UiText.Get("Auth.Password", "Password");
        public static string SignInButton => UiText.Get("Auth.SignInButton", "Sign in");
        public static string CredentialsRequired => UiText.Get("Auth.CredentialsRequired", "Email and password are required.");
        public static string InvalidCredentials => UiText.Get("Auth.InvalidCredentials", "The email or password is incorrect.");
        public static string AccountLocked => UiText.Get("Auth.AccountLocked", "The account is temporarily locked. Please try again later.");
        public static string MfaTitle => UiText.Get("Auth.MfaTitle", "Verification required");
        public static string MfaSubtitle => UiText.Get("Auth.MfaSubtitle", "Enter an authenticator code or a recovery code to finish signing in.");
        public static string MfaCode => UiText.Get("Auth.MfaCode", "Authenticator or recovery code");
        public static string MfaCodeRequired => UiText.Get("Auth.MfaCodeRequired", "Enter the verification code.");
        public static string VerifyMfaButton => UiText.Get("Auth.VerifyMfaButton", "Verify and sign in");
        public static string BackToPassword => UiText.Get("Auth.BackToPassword", "Use a different account");
        public static string InvalidMfaCode => UiText.Get("Auth.InvalidMfaCode", "The verification code is not valid.");
        public static string ForgotPasswordLink => UiText.Get("Auth.ForgotPasswordLink", "Forgot password?");
        public static string ForgotPasswordTitle => UiText.Get("Auth.ForgotPasswordTitle", "Reset your password");
        public static string ForgotPasswordSubtitle => UiText.Get("Auth.ForgotPasswordSubtitle", "Enter your email and we will send reset instructions if the account exists.");
        public static string ForgotPasswordButton => UiText.Get("Auth.ForgotPasswordButton", "Send reset link");
        public static string ForgotPasswordSuccess => UiText.Get("Auth.ForgotPasswordSuccess", "If that account exists, a reset link has been sent.");
        public static string EmailRequired => UiText.Get("Auth.EmailRequired", "Email is required.");
        public static string ResetPasswordTitle => UiText.Get("Auth.ResetPasswordTitle", "Choose a new password");
        public static string ResetPasswordSubtitle => UiText.Get("Auth.ResetPasswordSubtitle", "Use the reset link from your email to set a new password.");
        public static string ResetPasswordButton => UiText.Get("Auth.ResetPasswordButton", "Reset password");
        public static string ResetPasswordSuccess => UiText.Get("Auth.ResetPasswordSuccess", "Your password has been reset. You can sign in now.");
        public static string ResetPasswordInvalidLink => UiText.Get("Auth.ResetPasswordInvalidLink", "The reset link is invalid or has expired.");
        public static string ConfirmEmailChangeTitle => UiText.Get("Auth.ConfirmEmailChangeTitle", "Confirm email change");
        public static string ConfirmEmailChangeSubtitle => UiText.Get("Auth.ConfirmEmailChangeSubtitle", "Confirm the new sign-in email for your portal account.");
        public static string ConfirmEmailChangeButton => UiText.Get("Auth.ConfirmEmailChangeButton", "Confirm email");
        public static string ConfirmEmailChangeSuccess => UiText.Get("Auth.ConfirmEmailChangeSuccess", "Your email address has been confirmed. You can sign in with the new address.");
        public static string ConfirmEmailChangeInvalidLink => UiText.Get("Auth.ConfirmEmailChangeInvalidLink", "The email confirmation link is invalid or has expired.");
        public static string ActivateTitle => UiText.Get("Auth.ActivateTitle", "Activate your account");
        public static string ActivateSubtitle => UiText.Get("Auth.ActivateSubtitle", "Set a password to finish setting up your account.");
        public static string ActivationToken => UiText.Get("Auth.ActivationToken", "Activation code");
        public static string ActivationTokenRequired => UiText.Get("Auth.ActivationTokenRequired", "Activation code is required.");
        public static string NewPassword => UiText.Get("Auth.NewPassword", "New password");
        public static string ConfirmPassword => UiText.Get("Auth.ConfirmPassword", "Confirm password");
        public static string ActivateButton => UiText.Get("Auth.ActivateButton", "Activate account");
        public static string ActivationSuccess => UiText.Get("Auth.ActivationSuccess", "Your account is active. You can sign in now.");
        public static string PasswordsDoNotMatch => UiText.Get("Auth.PasswordsDoNotMatch", "The passwords do not match.");
        public static string PasswordTooShort => UiText.Get("Auth.PasswordTooShort", "Use at least 8 characters.");
        public static string GoToSignIn => UiText.Get("Auth.GoToSignIn", "Go to sign in");
        public static string BrandHeadline => UiText.Get("Auth.BrandHeadline", "Operations, under control.");
        public static string BrandSubtext => UiText.Get("Auth.BrandSubtext", "Manage your people, roles, and access from one secure, bilingual portal.");
        public static string BrandPointAccess => UiText.Get("Auth.BrandPointAccess", "Granular role-based access control");
        public static string BrandPointSessions => UiText.Get("Auth.BrandPointSessions", "Live session and device management");
        public static string BrandPointBilingual => UiText.Get("Auth.BrandPointBilingual", "Full Arabic and English support");
        public static string Copyright => UiText.Get("Auth.Copyright", "(c) 2026 Operations System. All rights reserved.");
    }

    public static class Nav
    {
        public static string Dashboard => UiText.Get("Nav.Dashboard", "Dashboard");
        public static string Users => UiText.Get("Nav.Users", "Users");
        public static string Roles => UiText.Get("Nav.Roles", "Roles");
        public static string Audit => UiText.Get("Nav.Audit", "Audit log");
        public static string Account => UiText.Get("Nav.Account", "Account");
        public static string Overview => UiText.Get("Nav.Overview", "Overview");
        public static string Administration => UiText.Get("Nav.Administration", "Administration");
        public static string MasterData => UiText.Get("Nav.MasterData", "Master data");
        public static string Countries => UiText.Get("Nav.Countries", "Countries");
        public static string ManpowerTypes => UiText.Get("Nav.ManpowerTypes", "Manpower types");
        public static string Licenses => UiText.Get("Nav.Licenses", "Licenses");
        public static string Services => UiText.Get("Nav.Services", "Services");
        public static string OperationTypes => UiText.Get("Nav.OperationTypes", "Operation types");
        public static string AircraftTypes => UiText.Get("Nav.AircraftTypes", "Aircraft types");
        public static string Tools => UiText.Get("Nav.Tools", "Tools");
        public static string Materials => UiText.Get("Nav.Materials", "Materials");
        public static string GeneralSupports => UiText.Get("Nav.GeneralSupports", "General supports");
        public static string Stations => UiText.Get("Nav.Stations", "Stations");
        public static string Customers => UiText.Get("Nav.Customers", "Customers");
        public static string StaffMembers => UiText.Get("Nav.StaffMembers", "Staff members");
        public static string ToggleMenu => UiText.Get("Nav.ToggleMenu", "Toggle navigation");
        public static string Language => UiText.Get("Nav.Language", "Language");
    }

    public static class Audit
    {
        public static string Title => UiText.Get("Audit.Title", "Audit log");
        public static string Description => UiText.Get("Audit.Description", "Permanent, append-only history of business and security changes.");
        public static string When => UiText.Get("Audit.When", "When");
        public static string Actor => UiText.Get("Audit.Actor", "Actor");
        public static string System => UiText.Get("Audit.System", "System");
        public static string Module => UiText.Get("Audit.Module", "Module");
        public static string Entity => UiText.Get("Audit.Entity", "Entity");
        public static string Action => UiText.Get("Audit.Action", "Action");
        public static string Empty => UiText.Get("Audit.Empty", "No audit activity recorded yet.");
        public static string Changes => UiText.Get("Audit.Changes", "Field changes");
        public static string Field => UiText.Get("Audit.Field", "Field");
        public static string Before => UiText.Get("Audit.Before", "Before");
        public static string After => UiText.Get("Audit.After", "After");
        public static string NoChanges => UiText.Get("Audit.NoChanges", "No field-level changes were recorded for this entry.");
        public static string ActivityTitle => UiText.Get("Audit.ActivityTitle", "Recent activity");
        public static string HistoryTitle => UiText.Get("Audit.HistoryTitle", "History");
        public static string HistoryDescription => UiText.Get("Audit.HistoryDescription", "Changes made to this user account.");
        public static string ActivityDescription => UiText.Get("Audit.ActivityDescription", "Actions performed by this user across the system.");
        public static string HistoryEmpty => UiText.Get("Audit.HistoryEmpty", "No changes have been recorded for this user account yet.");
        public static string ActivityEmpty => UiText.Get("Audit.ActivityEmpty", "No actions have been recorded for this user yet.");
        public static string ViewDetails => UiText.Get("Audit.ViewDetails", "View");
        public static string DetailsTitle => UiText.Get("Audit.DetailsTitle", "Audit details");
        public static string CorrelationId => UiText.Get("Audit.CorrelationId", "Correlation ID");
        public static string Metadata => UiText.Get("Audit.Metadata", "Additional details");
    }

    public static class Dashboard
    {
        public static string WelcomeBack => UiText.Get("Dashboard.WelcomeBack", "Welcome back");
        public static string Overview => UiText.Get("Dashboard.Overview", "Overview");
        public static string IdentityCard => UiText.Get("Dashboard.IdentityCard", "Account access");
        public static string PermissionsCard => UiText.Get("Dashboard.PermissionsCard", "Permissions");
        public static string SessionsCard => UiText.Get("Dashboard.SessionsCard", "Active sessions");
        public static string ManageUsers => UiText.Get("Dashboard.ManageUsers", "Manage users");
        public static string ManageRoles => UiText.Get("Dashboard.ManageRoles", "Manage roles");
    }

    public static class Account
    {
        public static string Title => UiText.Get("Account.Title", "Account");
        public static string Profile => UiText.Get("Account.Profile", "Profile");
        public static string Role => UiText.Get("Account.Role", "Role");
        public static string Permissions => UiText.Get("Account.Permissions", "Permissions");
        public static string DisplayName => UiText.Get("Account.DisplayName", "Display name");
        public static string ProfileUpdated => UiText.Get("Account.ProfileUpdated", "Your profile has been updated.");
        public static string ChangePassword => UiText.Get("Account.ChangePassword", "Change password");
        public static string CurrentPassword => UiText.Get("Account.CurrentPassword", "Current password");
        public static string PasswordChanged => UiText.Get("Account.PasswordChanged", "Your password has been changed. Please sign in again.");
        public static string Sessions => UiText.Get("Account.Sessions", "Sessions");
        public static string SessionsDescription => UiText.Get("Account.SessionsDescription", "Devices currently signed in with your account.");
        public static string ThisDevice => UiText.Get("Account.ThisDevice", "This device");
        public static string SignOutOthers => UiText.Get("Account.SignOutOthers", "Sign out other sessions");
        public static string RevokeSession => UiText.Get("Account.RevokeSession", "Revoke");
        public static string MfaTitle => UiText.Get("Account.MfaTitle", "Multi-factor authentication");
        public static string MfaRequiredHint => UiText.Get("Account.MfaRequiredHint", "This account must enroll MFA to meet administrator security requirements.");
        public static string MfaOptionalHint => UiText.Get("Account.MfaOptionalHint", "Add an authenticator app to protect this account.");
        public static string MfaEnabledHint => UiText.Get("Account.MfaEnabledHint", "MFA is enabled. Sign-in now requires an authenticator or recovery code.");
        public static string StartMfa => UiText.Get("Account.StartMfa", "Start MFA setup");
        public static string ConfirmMfa => UiText.Get("Account.ConfirmMfa", "Confirm MFA");
        public static string MfaSecret => UiText.Get("Account.MfaSecret", "Secret");
        public static string MfaOtpAuthUri => UiText.Get("Account.MfaOtpAuthUri", "Authenticator URI");
        public static string MfaEnrollmentHint => UiText.Get("Account.MfaEnrollmentHint", "Scan the QR code with your authenticator app, or enter the setup key manually, then confirm with the current code.");
        public static string MfaScanTitle => UiText.Get("Account.MfaScanTitle", "Scan QR code");
        public static string MfaScanHint => UiText.Get("Account.MfaScanHint", "Use Google Authenticator, Microsoft Authenticator, 1Password, or another TOTP app.");
        public static string MfaManualTitle => UiText.Get("Account.MfaManualTitle", "Enter setup key manually");
        public static string MfaManualHint => UiText.Get("Account.MfaManualHint", "Use this key only if you cannot scan the QR code.");
        public static string MfaEnabled => UiText.Get("Account.MfaEnabled", "MFA enabled.");
        public static string TurnOffMfa => UiText.Get("Account.TurnOffMfa", "Turn off MFA");
        public static string ConfirmDisableMfa => UiText.Get("Account.ConfirmDisableMfa", "Turn off MFA for this account? Future sign-ins will only require the password.");
        public static string MfaDisabled => UiText.Get("Account.MfaDisabled", "MFA turned off.");
        public static string RecoveryCodesTitle => UiText.Get("Account.RecoveryCodesTitle", "Save these recovery codes");
        public static string RecoveryCodesHint => UiText.Get("Account.RecoveryCodesHint", "Each code can be used once if you lose access to your authenticator app.");
    }

    public static class Users
    {
        public static string Title => UiText.Get("Users.Title", "Users");
        public static string Description => UiText.Get("Users.Description", "Invite, edit, and manage portal accounts.");
        public static string Invite => UiText.Get("Users.Invite", "Invite user");
        public static string Empty => UiText.Get("Users.Empty", "No users match your filters.");
        public static string CountLabel => UiText.Get("Users.CountLabel", "users");
        public static string SearchPlaceholder => UiText.Get("Users.SearchPlaceholder", "Search by name or email");
        public static string DisplayName => UiText.Get("Users.DisplayName", "Name");
        public static string Email => UiText.Get("Users.Email", "Email");
        public static string Status => UiText.Get("Users.Status", "Status");
        public static string Role => UiText.Get("Users.Role", "Role");
        public static string LastLogin => UiText.Get("Users.LastLogin", "Last login");
        public static string Created => UiText.Get("Users.Created", "Created");
        public static string AllStatuses => UiText.Get("Users.AllStatuses", "All statuses");
        public static string AllRoles => UiText.Get("Users.AllRoles", "All roles");
        public static string FilterByStatus => UiText.Get("Users.FilterByStatus", "All statuses");
        public static string FilterByUserType => UiText.Get("Users.FilterByUserType", "All types");
        public static string FilterByRole => UiText.Get("Users.FilterByRole", "All roles");
        public static string UserType => UiText.Get("Users.UserType", "Account type");
        public static string TypeSystemAdministrator => UiText.Get("Users.TypeSystemAdministrator", "System administrator");
        public static string TypeStationStaff => UiText.Get("Users.TypeStationStaff", "Station staff");
        public static string TypeCustomerContact => UiText.Get("Users.TypeCustomerContact", "Customer contact");
        public static string Lock => UiText.Get("Users.Lock", "Lock");
        public static string Unlock => UiText.Get("Users.Unlock", "Unlock");
        public static string Suspend => UiText.Get("Users.Suspend", "Suspend");
        public static string RestoreAccess => UiText.Get("Users.RestoreAccess", "Restore access");
        public static string ResetMfa => UiText.Get("Users.ResetMfa", "Reset MFA");
        public static string Deactivate => UiText.Get("Users.Deactivate", "Deactivate");
        public static string ResendInvitation => UiText.Get("Users.ResendInvitation", "Resend invitation");
        public static string AssignRole => UiText.Get("Users.AssignRole", "Assign role");
        public static string LockedOut => UiText.Get("Users.LockedOut", "Locked out");
        public static string Never => UiText.Get("Users.Never", "Never");
        public static string InviteTitle => UiText.Get("Users.InviteTitle", "Invite a user");
        public static string InviteIntro => UiText.Get("Users.InviteIntro", "Send an invitation so this person can set up their portal account.");
        public static string InviteAdminNote => UiText.Get("Users.InviteAdminNote", "Direct user creation invites a System Administrator. Station staff and customer contacts are invited from their own records.");
        public static string DisplayNameRequired => UiText.Get("Users.DisplayNameRequired", "A display name is required.");
        public static string RoleRequired => UiText.Get("Users.RoleRequired", "Select a role to continue.");
        public static string NoAdminRoles => UiText.Get("Users.NoAdminRoles", "No System Administrator role is available.");
        public static string NoCompatibleRoles => UiText.Get("Users.NoCompatibleRoles", "No compatible role is available for this account type.");
        public static string InviteSuccess => UiText.Get("Users.InviteSuccess", "Invitation sent.");
        public static string InvitationDeliveryLabel => UiText.Get("Users.InvitationDeliveryLabel", "Invitation delivery status");
        public static string EditTitle => UiText.Get("Users.EditTitle", "Edit user");
        public static string AssignRoleTitle => UiText.Get("Users.AssignRoleTitle", "Assign role");
        public static string ConfirmDeactivate => UiText.Get("Users.ConfirmDeactivate", "Deactivate this user? They will be signed out of all sessions.");
        public static string ConfirmLock => UiText.Get("Users.ConfirmLock", "Lock this user out of signing in?");
        public static string ConfirmSuspend => UiText.Get("Users.ConfirmSuspend", "Suspend this user? They will lose access until restored.");
        public static string ConfirmRestoreAccess => UiText.Get("Users.ConfirmRestoreAccess", "Restore access for this user?");
        public static string ConfirmResetMfa => UiText.Get("Users.ConfirmResetMfa", "Reset MFA for this user? Active sessions will be revoked and they must enroll again.");
        public static string StatActiveSessions => UiText.Get("Users.StatActiveSessions", "Active sessions");
        public static string UserId => UiText.Get("Users.UserId", "User ID");
        public static string CopyId => UiText.Get("Users.CopyId", "Copy user ID");
        public static string CopyEmail => UiText.Get("Users.CopyEmail", "Copy email");
        public static string AccountStatus => UiText.Get("Users.AccountStatus", "Account status");
        public static string Permissions => UiText.Get("Users.Permissions", "Permissions");
        public static string Security => UiText.Get("Users.Security", "Security");
        public static string MemberSince => UiText.Get("Users.MemberSince", "Member since");
        public static string LastUpdated => UiText.Get("Users.LastUpdated", "Last updated");
        public static string LockStatus => UiText.Get("Users.LockStatus", "Lock status");
        public static string NotLocked => UiText.Get("Users.NotLocked", "Not locked");
        public static string LockedUntil => UiText.Get("Users.LockedUntil", "Locked until {0}");
        public static string LockedIndefinitely => UiText.Get("Users.LockedIndefinitely", "Locked indefinitely - manual unlock required");
        public static string AccessScope => UiText.Get("Users.AccessScope", "Access scope");
        public static string RecentDevice => UiText.Get("Users.RecentDevice", "Recent device");
        public static string Mfa => UiText.Get("Users.Mfa", "MFA");
        public static string MfaEnabled => UiText.Get("Users.MfaEnabled", "Enabled");
        public static string MfaRequired => UiText.Get("Users.MfaRequired", "Required");
        public static string MfaNotEnabled => UiText.Get("Users.MfaNotEnabled", "Not enabled");
        public static string StatusActiveDesc => UiText.Get("Users.StatusActiveDesc", "This account is active and can access all assigned resources.");
        public static string StatusInvitedDesc => UiText.Get("Users.StatusInvitedDesc", "This account has been invited and is awaiting activation.");
        public static string StatusSuspendedDesc => UiText.Get("Users.StatusSuspendedDesc", "This account is suspended and cannot sign in until access is restored.");
        public static string StatusDeactivatedDesc => UiText.Get("Users.StatusDeactivatedDesc", "This account is deactivated and cannot sign in.");
        public static string StatusLockedDesc => UiText.Get("Users.StatusLockedDesc", "This account is temporarily locked out of signing in.");
        public static string ScopeFullAccess => UiText.Get("Users.ScopeFullAccess", "Full system access");
        public static string ScopeStation => UiText.Get("Users.ScopeStation", "Station-scoped access");
        public static string ScopeCustomer => UiText.Get("Users.ScopeCustomer", "Customer-scoped access");
    }

    public static class Roles
    {
        public static string Title => UiText.Get("Roles.Title", "Roles");
        public static string Description => UiText.Get("Roles.Description", "Define roles and the permissions they grant.");
        public static string Create => UiText.Get("Roles.Create", "New role");
        public static string Empty => UiText.Get("Roles.Empty", "No roles match your search.");
        public static string CountLabel => UiText.Get("Roles.CountLabel", "roles");
        public static string SearchPlaceholder => UiText.Get("Roles.SearchPlaceholder", "Search roles");
        public static string Name => UiText.Get("Roles.Name", "Name");
        public static string RoleDescription => UiText.Get("Roles.RoleDescription", "Description");
        public static string CompatibleUserType => UiText.Get("Roles.CompatibleUserType", "Account type");
        public static string System => UiText.Get("Roles.System", "System");
        public static string PermissionCount => UiText.Get("Roles.PermissionCount", "Permissions");
        public static string UserCount => UiText.Get("Roles.UserCount", "Users");
        public static string RoleType => UiText.Get("Roles.RoleType", "Type");
        public static string Custom => UiText.Get("Roles.Custom", "Custom");
        public static string NoDescription => UiText.Get("Roles.NoDescription", "No description");
        public static string CreateTitle => UiText.Get("Roles.CreateTitle", "Create role");
        public static string EditTitle => UiText.Get("Roles.EditTitle", "Edit role");
        public static string FormIntro => UiText.Get("Roles.FormIntro", "Name the role and choose which account type it applies to.");
        public static string Permissions => UiText.Get("Roles.Permissions", "Permissions");
        public static string PermissionsDescription => UiText.Get("Roles.PermissionsDescription", "Select the permissions this role grants.");
        public static string PermissionsSaved => UiText.Get("Roles.PermissionsSaved", "Permissions updated.");
        public static string ReferenceOptionsHint => UiText.Get("Roles.ReferenceOptionsHint", "Flight forms need reference lookups (dropdown options). Add the \"view-options\" permission under Reference so users of this role can load those forms.");
        public static string SystemRoleLocked => UiText.Get("Roles.SystemRoleLocked", "System roles cannot be edited or deleted.");
        public static string ConfirmDelete => UiText.Get("Roles.ConfirmDelete", "Delete this role? Users must not be assigned to it.");
        public static string Created => UiText.Get("Roles.Created", "Role created.");
        public static string Updated => UiText.Get("Roles.Updated", "Role updated.");
        public static string Deleted => UiText.Get("Roles.Deleted", "Role deleted.");
    }

    public static class Sessions
    {
        public static string Title => UiText.Get("Sessions.Title", "Sessions");
        public static string Created => UiText.Get("Sessions.Created", "Signed in");
        public static string Expires => UiText.Get("Sessions.Expires", "Expires");
        public static string Device => UiText.Get("Sessions.Device", "Device");
        public static string IpAddress => UiText.Get("Sessions.IpAddress", "IP address");
        public static string State => UiText.Get("Sessions.State", "State");
        public static string Active => UiText.Get("Sessions.Active", "Active");
        public static string Revoked => UiText.Get("Sessions.Revoked", "Revoked");
        public static string RevokeAll => UiText.Get("Sessions.RevokeAll", "Revoke all sessions");
        public static string Empty => UiText.Get("Sessions.Empty", "No sessions found.");
        public static string EmptyHint => UiText.Get("Sessions.EmptyHint", "Active devices for this user will appear here once they sign in.");
        public static string Current => UiText.Get("Sessions.Current", "Current");
        public static string Expired => UiText.Get("Sessions.Expired", "Expired");
        public static string Browser => UiText.Get("Sessions.Browser", "Browser");
        public static string LastActivity => UiText.Get("Sessions.LastActivity", "Signed in");
        public static string CurrentSession => UiText.Get("Sessions.CurrentSession", "Current session");
        public static string OtherSessions => UiText.Get("Sessions.OtherSessions", "Other sessions");
        public static string UnknownDevice => UiText.Get("Sessions.UnknownDevice", "Unknown device");
        public static string CurrentDeviceBadge => UiText.Get("Sessions.CurrentDeviceBadge", "Current device");
    }

    public static class Countries
    {
        public static string Title => UiText.Get("Countries.Title", "Countries");
        public static string Description => UiText.Get("Countries.Description", "Maintain the ISO country list used across the system.");
        public static string Create => UiText.Get("Countries.Create", "New country");
        public static string Empty => UiText.Get("Countries.Empty", "No countries match your filters.");
        public static string CountLabel => UiText.Get("Countries.CountLabel", "countries");
        public static string SearchPlaceholder => UiText.Get("Countries.SearchPlaceholder", "Search by name or code");
        public static string Name => UiText.Get("Countries.Name", "Name");
        public static string IsoCode => UiText.Get("Countries.IsoCode", "ISO code");
        public static string Status => UiText.Get("Countries.Status", "Status");
        public static string Active => UiText.Get("Countries.Active", "Active");
        public static string Inactive => UiText.Get("Countries.Inactive", "Inactive");
        public static string AllStatuses => UiText.Get("Countries.AllStatuses", "All statuses");
        public static string Activate => UiText.Get("Countries.Activate", "Activate");
        public static string Deactivate => UiText.Get("Countries.Deactivate", "Deactivate");
        public static string Created => UiText.Get("Countries.Created", "Created");
        public static string Updated => UiText.Get("Countries.Updated", "Updated");
        public static string CreateTitle => UiText.Get("Countries.CreateTitle", "Create country");
        public static string EditTitle => UiText.Get("Countries.EditTitle", "Edit country");
        public static string FormIntro => UiText.Get("Countries.FormIntro", "Maintain the country name and its ISO code.");
        public static string NameRequired => UiText.Get("Countries.NameRequired", "A country name is required.");
        public static string IsoCodeRequired => UiText.Get("Countries.IsoCodeRequired", "A 2-letter ISO code is required.");
        public static string SavedCreate => UiText.Get("Countries.SavedCreate", "Country created.");
        public static string SavedUpdate => UiText.Get("Countries.SavedUpdate", "Country updated.");
        public static string Activated => UiText.Get("Countries.Activated", "Country activated.");
        public static string Deactivated => UiText.Get("Countries.Deactivated", "Country deactivated.");
        public static string ConfirmDeactivate => UiText.Get("Countries.ConfirmDeactivate", "Deactivate this country? It will no longer be selectable.");
    }

    public static class ManpowerTypes
    {
        public static string Title => UiText.Get("ManpowerTypes.Title", "Manpower types");
        public static string Description => UiText.Get("ManpowerTypes.Description", "Classify staff members by the work they perform.");
        public static string Create => UiText.Get("ManpowerTypes.Create", "New manpower type");
        public static string Empty => UiText.Get("ManpowerTypes.Empty", "No manpower types match your filters.");
        public static string CountLabel => UiText.Get("ManpowerTypes.CountLabel", "manpower types");
        public static string SearchPlaceholder => UiText.Get("ManpowerTypes.SearchPlaceholder", "Search by name");
        public static string Name => UiText.Get("ManpowerTypes.Name", "Name");
        public static string ManpowerDescription => UiText.Get("ManpowerTypes.ManpowerDescription", "Description");
        public static string NoDescription => UiText.Get("ManpowerTypes.NoDescription", "No description");
        public static string Status => UiText.Get("ManpowerTypes.Status", "Status");
        public static string Active => UiText.Get("ManpowerTypes.Active", "Active");
        public static string Inactive => UiText.Get("ManpowerTypes.Inactive", "Inactive");
        public static string AllStatuses => UiText.Get("ManpowerTypes.AllStatuses", "All statuses");
        public static string Activate => UiText.Get("ManpowerTypes.Activate", "Activate");
        public static string Deactivate => UiText.Get("ManpowerTypes.Deactivate", "Deactivate");
        public static string Created => UiText.Get("ManpowerTypes.Created", "Created");
        public static string Updated => UiText.Get("ManpowerTypes.Updated", "Updated");
        public static string CreateTitle => UiText.Get("ManpowerTypes.CreateTitle", "Create manpower type");
        public static string EditTitle => UiText.Get("ManpowerTypes.EditTitle", "Edit manpower type");
        public static string FormIntro => UiText.Get("ManpowerTypes.FormIntro", "Name the manpower type and optionally describe it.");
        public static string NameRequired => UiText.Get("ManpowerTypes.NameRequired", "A name is required.");
        public static string SavedCreate => UiText.Get("ManpowerTypes.SavedCreate", "Manpower type created.");
        public static string SavedUpdate => UiText.Get("ManpowerTypes.SavedUpdate", "Manpower type updated.");
        public static string Activated => UiText.Get("ManpowerTypes.Activated", "Manpower type activated.");
        public static string Deactivated => UiText.Get("ManpowerTypes.Deactivated", "Manpower type deactivated.");
        public static string ConfirmDeactivate => UiText.Get("ManpowerTypes.ConfirmDeactivate", "Deactivate this manpower type? It will no longer be selectable.");
    }

    public static class Licenses
    {
        public static string Title => UiText.Get("Licenses.Title", "Licenses");
        public static string Description => UiText.Get("Licenses.Description", "Manage the licenses and certifications staff can hold.");
        public static string Create => UiText.Get("Licenses.Create", "New license");
        public static string Empty => UiText.Get("Licenses.Empty", "No licenses match your filters.");
        public static string CountLabel => UiText.Get("Licenses.CountLabel", "licenses");
        public static string SearchPlaceholder => UiText.Get("Licenses.SearchPlaceholder", "Search by code or name");
        public static string Code => UiText.Get("Licenses.Code", "Code");
        public static string Name => UiText.Get("Licenses.Name", "Name");
        public static string LicenseDescription => UiText.Get("Licenses.LicenseDescription", "Description");
        public static string NoDescription => UiText.Get("Licenses.NoDescription", "No description");
        public static string Status => UiText.Get("Licenses.Status", "Status");
        public static string Active => UiText.Get("Licenses.Active", "Active");
        public static string Inactive => UiText.Get("Licenses.Inactive", "Inactive");
        public static string AllStatuses => UiText.Get("Licenses.AllStatuses", "All statuses");
        public static string Activate => UiText.Get("Licenses.Activate", "Activate");
        public static string Deactivate => UiText.Get("Licenses.Deactivate", "Deactivate");
        public static string Created => UiText.Get("Licenses.Created", "Created");
        public static string Updated => UiText.Get("Licenses.Updated", "Updated");
        public static string CreateTitle => UiText.Get("Licenses.CreateTitle", "Create license");
        public static string EditTitle => UiText.Get("Licenses.EditTitle", "Edit license");
        public static string FormIntro => UiText.Get("Licenses.FormIntro", "Define the license code, name and an optional description.");
        public static string CodeRequired => UiText.Get("Licenses.CodeRequired", "A license code is required (2-10 letters or digits).");
        public static string NameRequired => UiText.Get("Licenses.NameRequired", "A license name is required.");
        public static string CodeImmutable => UiText.Get("Licenses.CodeImmutable", "The code cannot be changed after creation.");
        public static string SavedCreate => UiText.Get("Licenses.SavedCreate", "License created.");
        public static string SavedUpdate => UiText.Get("Licenses.SavedUpdate", "License updated.");
        public static string Activated => UiText.Get("Licenses.Activated", "License activated.");
        public static string Deactivated => UiText.Get("Licenses.Deactivated", "License deactivated.");
        public static string ConfirmDeactivate => UiText.Get("Licenses.ConfirmDeactivate", "Deactivate this license? It will no longer be selectable.");
    }

    public static class Stations
    {
        public static string Title => UiText.Get("Stations.Title", "Stations");
        public static string Description => UiText.Get("Stations.Description", "Manage the airport stations the operation works at.");
        public static string Create => UiText.Get("Stations.Create", "New station");
        public static string Empty => UiText.Get("Stations.Empty", "No stations match your filters.");
        public static string CountLabel => UiText.Get("Stations.CountLabel", "stations");
        public static string SearchPlaceholder => UiText.Get("Stations.SearchPlaceholder", "Filter stations");
        public static string IataCode => UiText.Get("Stations.IataCode", "IATA");
        public static string IcaoCode => UiText.Get("Stations.IcaoCode", "ICAO");
        public static string Name => UiText.Get("Stations.Name", "Name");
        public static string City => UiText.Get("Stations.City", "City");
        public static string Country => UiText.Get("Stations.Country", "Country");
        public static string NoIcao => UiText.Get("Stations.NoIcao", "\u2014");
        public static string Status => UiText.Get("Stations.Status", "Status");
        public static string Active => UiText.Get("Stations.Active", "Active");
        public static string Inactive => UiText.Get("Stations.Inactive", "Inactive");
        public static string AllStatuses => UiText.Get("Stations.AllStatuses", "All statuses");
        public static string Activate => UiText.Get("Stations.Activate", "Activate");
        public static string Deactivate => UiText.Get("Stations.Deactivate", "Deactivate");
        public static string Created => UiText.Get("Stations.Created", "Created");
        public static string Updated => UiText.Get("Stations.Updated", "Updated");
        public static string CreateTitle => UiText.Get("Stations.CreateTitle", "Create station");
        public static string EditTitle => UiText.Get("Stations.EditTitle", "Edit station");
        public static string FormIntro => UiText.Get("Stations.FormIntro", "Set the airport codes, name, city and country for this station.");
        public static string IataRequired => UiText.Get("Stations.IataRequired", "A 3-letter IATA code is required.");
        public static string IcaoInvalid => UiText.Get("Stations.IcaoInvalid", "ICAO code must be exactly four letters.");
        public static string NameRequired => UiText.Get("Stations.NameRequired", "A station name is required.");
        public static string CityRequired => UiText.Get("Stations.CityRequired", "A city is required.");
        public static string CountryRequired => UiText.Get("Stations.CountryRequired", "An active country is required.");
        public static string SavedCreate => UiText.Get("Stations.SavedCreate", "Station created.");
        public static string SavedUpdate => UiText.Get("Stations.SavedUpdate", "Station updated.");
        public static string Activated => UiText.Get("Stations.Activated", "Station activated.");
        public static string Deactivated => UiText.Get("Stations.Deactivated", "Station deactivated.");
        public static string ConfirmDeactivate => UiText.Get("Stations.ConfirmDeactivate", "Deactivate this station? Linked staff lose portal access.");
        public static string StaffMembers => UiText.Get("Stations.StaffMembers", "Staff members");
        public static string StepDetails => UiText.Get("Stations.StepDetails", "Station details");
        public static string StepStaff => UiText.Get("Stations.StepStaff", "Add staff (optional)");
        public static string StepReview => UiText.Get("Stations.StepReview", "Review & create");
        public static string SelectCountry => UiText.Get("Stations.SelectCountry", "Select country");
        public static string CodesHelpTitle => UiText.Get("Stations.CodesHelpTitle", "Not sure about the codes?");
        public static string CodesHelpText => UiText.Get("Stations.CodesHelpText", "You can confirm IATA and ICAO codes from the official airport code references.");
        public static string StaffStepIntro => UiText.Get("Stations.StaffStepIntro", "Add staff who will be associated with this station.");
        public static string AddAnotherStaff => UiText.Get("Stations.AddAnotherStaff", "Add another staff member");
        public static string StaffLaterHint => UiText.Get("Stations.StaffLaterHint", "You can add staff members later from the staff members section.");
        public static string ReviewTitle => UiText.Get("Stations.ReviewTitle", "Review your station");
        public static string ReviewIntro => UiText.Get("Stations.ReviewIntro", "Please review the information below before creating the station.");
        public static string StationInformation => UiText.Get("Stations.StationInformation", "Station information");
        public static string StaffAssignments => UiText.Get("Stations.StaffAssignments", "Staff assignments");
        public static string ReviewNote => UiText.Get("Stations.ReviewNote", "You can review and edit any step before creating the station.");
        public static string Next => UiText.Get("Stations.Next", "Next");
        public static string Previous => UiText.Get("Stations.Previous", "Back");
        public static string AddStaff => UiText.Get("Stations.AddStaff", "Add staff member");
        public static string RemoveStaff => UiText.Get("Stations.RemoveStaff", "Remove");
        public static string StaffName => UiText.Get("Stations.StaffName", "Full name");
        public static string StaffEmail => UiText.Get("Stations.StaffEmail", "Email");
        public static string ManpowerType => UiText.Get("Stations.ManpowerType", "Manpower type");
        public static string GrantPortal => UiText.Get("Stations.GrantPortal", "Grant portal access");
        public static string PortalRole => UiText.Get("Stations.PortalRole", "Portal role");
        public static string NoStaffYet => UiText.Get("Stations.NoStaffYet", "No staff added. You can add staff now or later from the station page.");
        public static string ReviewStaffCount => UiText.Get("Stations.ReviewStaffCount", "{0} staff member(s) will be created with this station.");
        public static string StaffIncomplete => UiText.Get("Stations.StaffIncomplete", "Each staff row needs a name, email, and manpower type.");
        public static string StaffRoleRequired => UiText.Get("Stations.StaffRoleRequired", "Select a portal role or turn off portal access for this staff member.");
        public static string StaffMembersEmpty => UiText.Get("Stations.StaffMembersEmpty", "No staff members are assigned to this station yet.");
        public static string AddStaffMember => UiText.Get("Stations.AddStaffMember", "Add staff member");
        public static string StatStaff => UiText.Get("Stations.StatStaff", "Staff members");
        public static string StatActiveStaff => UiText.Get("Stations.StatActiveStaff", "Active staff");
        public static string StaffCountFormat => UiText.Get("Stations.StaffCountFormat", "{0} staff");
    }

    public static class Customers
    {
        public static string Title => UiText.Get("Customers.Title", "Customers");
        public static string Description => UiText.Get("Customers.Description", "Manage the airline customers the operation serves.");
        public static string Create => UiText.Get("Customers.Create", "New customer");
        public static string Empty => UiText.Get("Customers.Empty", "No customers match your filters.");
        public static string CountLabel => UiText.Get("Customers.CountLabel", "customers");
        public static string SearchPlaceholder => UiText.Get("Customers.SearchPlaceholder", "Search by code or name");
        public static string IataCode => UiText.Get("Customers.IataCode", "IATA");
        public static string IcaoCode => UiText.Get("Customers.IcaoCode", "ICAO");
        public static string Name => UiText.Get("Customers.Name", "Name");
        public static string Country => UiText.Get("Customers.Country", "Country");
        public static string NoIcao => UiText.Get("Customers.NoIcao", "-");
        public static string OfficialEmail => UiText.Get("Customers.OfficialEmail", "Official email");
        public static string OfficialPhone => UiText.Get("Customers.OfficialPhone", "Official phone");
        public static string Status => UiText.Get("Customers.Status", "Status");
        public static string Active => UiText.Get("Customers.Active", "Active");
        public static string Inactive => UiText.Get("Customers.Inactive", "Inactive");
        public static string AllStatuses => UiText.Get("Customers.AllStatuses", "All statuses");
        public static string Activate => UiText.Get("Customers.Activate", "Activate");
        public static string Deactivate => UiText.Get("Customers.Deactivate", "Deactivate");
        public static string Created => UiText.Get("Customers.Created", "Created");
        public static string Updated => UiText.Get("Customers.Updated", "Updated");
        public static string CreateTitle => UiText.Get("Customers.CreateTitle", "Create customer");
        public static string EditTitle => UiText.Get("Customers.EditTitle", "Edit customer");
        public static string StepDetails => UiText.Get("Customers.StepDetails", "Customer details");
        public static string StepAddress => UiText.Get("Customers.StepAddress", "Address");
        public static string StepContacts => UiText.Get("Customers.StepContacts", "Contacts");
        public static string StepReview => UiText.Get("Customers.StepReview", "Review & create");
        public static string Next => UiText.Get("Customers.Next", "Next");
        public static string Previous => UiText.Get("Customers.Previous", "Back");
        public static string ReviewSummary => UiText.Get("Customers.ReviewSummary", "{0} - {1}. {2} contact(s).");
        public static string FormIntro => UiText.Get("Customers.FormIntro", "Capture the airline codes, official contact details, address and contacts.");
        public static string EditFormIntro => UiText.Get("Customers.EditFormIntro", "Update the airline codes, official contact details, and address.");
        public static string IataRequired => UiText.Get("Customers.IataRequired", "A 2-character IATA code is required.");
        public static string IcaoInvalid => UiText.Get("Customers.IcaoInvalid", "ICAO code must be exactly three letters.");
        public static string NameRequired => UiText.Get("Customers.NameRequired", "A customer name is required.");
        public static string CountryRequired => UiText.Get("Customers.CountryRequired", "An active country is required.");
        public static string SavedCreate => UiText.Get("Customers.SavedCreate", "Customer created.");
        public static string SavedUpdate => UiText.Get("Customers.SavedUpdate", "Customer updated.");
        public static string Activated => UiText.Get("Customers.Activated", "Customer activated.");
        public static string Deactivated => UiText.Get("Customers.Deactivated", "Customer deactivated.");
        public static string ConfirmDeactivate => UiText.Get("Customers.ConfirmDeactivate", "Deactivate this customer? Linked contacts lose portal access.");
        public static string Address => UiText.Get("Customers.Address", "Official address");
        public static string AddressLine1 => UiText.Get("Customers.AddressLine1", "Address line 1");
        public static string AddressLine2 => UiText.Get("Customers.AddressLine2", "Address line 2");
        public static string City => UiText.Get("Customers.City", "City");
        public static string Region => UiText.Get("Customers.Region", "State / region");
        public static string PostalCode => UiText.Get("Customers.PostalCode", "Postal code");
        public static string AddressLine1Required => UiText.Get("Customers.AddressLine1Required", "Address line 1 is required.");
        public static string AddressCityRequired => UiText.Get("Customers.AddressCityRequired", "Address city is required.");
        public static string ContactsCount => UiText.Get("Customers.ContactsCount", "contacts");
        public static string Contacts => UiText.Get("Customers.Contacts", "Contacts");
        public static string ContactsEmpty => UiText.Get("Customers.ContactsEmpty", "No contacts have been added yet.");
        public static string AddContact => UiText.Get("Customers.AddContact", "Add contact");
        public static string EditContact => UiText.Get("Customers.EditContact", "Edit contact");
        public static string ContactName => UiText.Get("Customers.ContactName", "Name");
        public static string ContactJobTitle => UiText.Get("Customers.ContactJobTitle", "Job title");
        public static string ContactEmail => UiText.Get("Customers.ContactEmail", "Email");
        public static string ContactPhone => UiText.Get("Customers.ContactPhone", "Phone");
        public static string ContactNameRequired => UiText.Get("Customers.ContactNameRequired", "A contact name is required.");
        public static string ContactEmailRequired => UiText.Get("Customers.ContactEmailRequired", "A valid contact email is required.");
        public static string ContactEmailDuplicate => UiText.Get("Customers.ContactEmailDuplicate", "Contact emails must be unique within the customer.");
        public static string ContactRemove => UiText.Get("Customers.ContactRemove", "Remove");
        public static string ContactRemoved => UiText.Get("Customers.ContactRemoved", "(will be removed)");
        public static string ContactAdded => UiText.Get("Customers.ContactAdded", "Contact added.");
        public static string ContactUpdated => UiText.Get("Customers.ContactUpdated", "Contact updated.");
        public static string ContactRemovedSuccess => UiText.Get("Customers.ContactRemovedSuccess", "Contact removed.");
        public static string ConfirmRemoveContact => UiText.Get("Customers.ConfirmRemoveContact", "Remove this contact? Linked portal access will be deactivated.");
        public static string PortalLinked => UiText.Get("Customers.PortalLinked", "Portal access");
        public static string OpenLinkedAccount => UiText.Get("Customers.OpenLinkedAccount", "Open linked account");
        public static string TabAddress => UiText.Get("Customers.TabAddress", "Address");
        public static string StatContacts => UiText.Get("Customers.StatContacts", "Contacts");
        public static string StatPortalAccounts => UiText.Get("Customers.StatPortalAccounts", "Portal accounts");
        public static string ContactsCountFormat => UiText.Get("Customers.ContactsCountFormat", "{0} contacts");
    }

    public static class StaffMembers
    {
        public static string Title => UiText.Get("StaffMembers.Title", "Staff members");
        public static string Description => UiText.Get("StaffMembers.Description", "Manage station staff, their manpower type, schedule and licenses.");
        public static string Create => UiText.Get("StaffMembers.Create", "New staff member");
        public static string Empty => UiText.Get("StaffMembers.Empty", "No staff members match your filters.");
        public static string CountLabel => UiText.Get("StaffMembers.CountLabel", "staff members");
        public static string SearchPlaceholder => UiText.Get("StaffMembers.SearchPlaceholder", "Search by name or email");
        public static string FullName => UiText.Get("StaffMembers.FullName", "Full name");
        public static string EmployeeId => UiText.Get("StaffMembers.EmployeeId", "Employee ID");
        public static string EmployeeIdRequired => UiText.Get("StaffMembers.EmployeeIdRequired", "An employee ID is required.");
        public static string EmployeeIdHintTitle => UiText.Get("StaffMembers.EmployeeIdHintTitle", "Use the official employee identifier");
        public static string EmployeeIdHint => UiText.Get("StaffMembers.EmployeeIdHint", "Employee IDs are mandatory and must be unique across all staff members.");
        public static string Email => UiText.Get("StaffMembers.Email", "Email");
        public static string Assignment => UiText.Get("StaffMembers.Assignment", "Station assignment");
        public static string SelectStation => UiText.Get("StaffMembers.SelectStation", "Select station");
        public static string SelectManpowerType => UiText.Get("StaffMembers.SelectManpowerType", "Select manpower type");
        public static string StepProfile => UiText.Get("StaffMembers.StepProfile", "Profile & assignment");
        public static string StepEmployment => UiText.Get("StaffMembers.StepEmployment", "Employment & access");
        public static string StepReview => UiText.Get("StaffMembers.StepReview", "Review & create");
        public static string Previous => UiText.Get("StaffMembers.Previous", "Back");
        public static string Next => UiText.Get("StaffMembers.Next", "Next");
        public static string ReviewTitle => UiText.Get("StaffMembers.ReviewTitle", "Review staff member");
        public static string ReviewIntro => UiText.Get("StaffMembers.ReviewIntro", "Confirm the information below before creating this staff member.");
        public static string ReviewNote => UiText.Get("StaffMembers.ReviewNote", "You can return to any step and make changes before creating the staff member.");
        public static string FixErrors => UiText.Get("StaffMembers.FixErrors", "Please fix the following errors before continuing");
        public static string Station => UiText.Get("StaffMembers.Station", "Station");
        public static string ManpowerType => UiText.Get("StaffMembers.ManpowerType", "Manpower type");
        public static string Status => UiText.Get("StaffMembers.Status", "Status");
        public static string Active => UiText.Get("StaffMembers.Active", "Active");
        public static string Inactive => UiText.Get("StaffMembers.Inactive", "Inactive");
        public static string AllStatuses => UiText.Get("StaffMembers.AllStatuses", "All statuses");
        public static string Activate => UiText.Get("StaffMembers.Activate", "Activate");
        public static string Deactivate => UiText.Get("StaffMembers.Deactivate", "Deactivate");
        public static string Created => UiText.Get("StaffMembers.Created", "Created");
        public static string Updated => UiText.Get("StaffMembers.Updated", "Updated");
        public static string None => UiText.Get("StaffMembers.None", "-");
        public static string CreateTitle => UiText.Get("StaffMembers.CreateTitle", "Create staff member");
        public static string EditTitle => UiText.Get("StaffMembers.EditTitle", "Edit staff member");
        public static string BasicInformation => UiText.Get("StaffMembers.BasicInformation", "Basic information");
        public static string FormIntro => UiText.Get("StaffMembers.FormIntro", "Update staff member information, schedule and licenses.");
        public static string ScheduleHint => UiText.Get("StaffMembers.ScheduleHint", "Select the days this staff member works.");
        public static string NameRequired => UiText.Get("StaffMembers.NameRequired", "A full name is required.");
        public static string EmailRequired => UiText.Get("StaffMembers.EmailRequired", "A valid email is required.");
        public static string StationRequired => UiText.Get("StaffMembers.StationRequired", "An active station is required.");
        public static string ManpowerTypeRequired => UiText.Get("StaffMembers.ManpowerTypeRequired", "An active manpower type is required.");
        public static string SavedCreate => UiText.Get("StaffMembers.SavedCreate", "Staff member created.");
        public static string SavedUpdate => UiText.Get("StaffMembers.SavedUpdate", "Staff member updated.");
        public static string Activated => UiText.Get("StaffMembers.Activated", "Staff member activated.");
        public static string Deactivated => UiText.Get("StaffMembers.Deactivated", "Staff member deactivated.");
        public static string ConfirmDeactivate => UiText.Get("StaffMembers.ConfirmDeactivate", "Deactivate this staff member? A linked portal account loses access.");
        public static string EmploymentContract => UiText.Get("StaffMembers.EmploymentContract", "Employment contract");
        public static string StartDate => UiText.Get("StaffMembers.StartDate", "Start date");
        public static string EndDate => UiText.Get("StaffMembers.EndDate", "End date");
        public static string EndBeforeStart => UiText.Get("StaffMembers.EndBeforeStart", "The end date cannot precede the start date.");
        public static string WorkingSchedule => UiText.Get("StaffMembers.WorkingSchedule", "Working schedule");
        public static string NoSchedule => UiText.Get("StaffMembers.NoSchedule", "No working schedule set.");
        public static string NoContract => UiText.Get("StaffMembers.NoContract", "No employment contract.");
        public static string Licenses => UiText.Get("StaffMembers.Licenses", "Licenses");
        public static string LicensesEmpty => UiText.Get("StaffMembers.LicensesEmpty", "No licenses assigned.");
        public static string AddLicense => UiText.Get("StaffMembers.AddLicense", "Add license");
        public static string License => UiText.Get("StaffMembers.License", "License");
        public static string LicenseNumber => UiText.Get("StaffMembers.LicenseNumber", "License number");
        public static string LicenseRequired => UiText.Get("StaffMembers.LicenseRequired", "Select a license for each assignment.");
        public static string LicenseNumberRequired => UiText.Get("StaffMembers.LicenseNumberRequired", "A license number is required for each assignment.");
        public static string LicenseDuplicate => UiText.Get("StaffMembers.LicenseDuplicate", "A staff member cannot hold the same license twice.");
        public static string PortalLinked => UiText.Get("StaffMembers.PortalLinked", "Portal access");
        public static string EditMember => UiText.Get("StaffMembers.EditMember", "Edit member");
        public static string TabOverview => UiText.Get("StaffMembers.TabOverview", "Overview");
        public static string TabLicenses => UiText.Get("StaffMembers.TabLicenses", "Licenses");
        public static string TabSchedule => UiText.Get("StaffMembers.TabSchedule", "Schedule");
        public static string TabPortalAccess => UiText.Get("StaffMembers.TabPortalAccess", "Portal access");
        public static string TabActivity => UiText.Get("StaffMembers.TabActivity", "Activity");
        public static string PersonalInformation => UiText.Get("StaffMembers.PersonalInformation", "Personal information");
        public static string EmploymentDetails => UiText.Get("StaffMembers.EmploymentDetails", "Employment details");
        public static string RecentActivity => UiText.Get("StaffMembers.RecentActivity", "Recent activity");
        public static string StatLicenses => UiText.Get("StaffMembers.StatLicenses", "Licenses");
        public static string StatWorkingDays => UiText.Get("StaffMembers.StatWorkingDays", "Working days");
        public static string StatPortalAccount => UiText.Get("StaffMembers.StatPortalAccount", "Portal account");
        public static string PortalLinkedShort => UiText.Get("StaffMembers.PortalLinkedShort", "Linked");
        public static string PortalNotLinkedShort => UiText.Get("StaffMembers.PortalNotLinkedShort", "Not created");
        public static string LicenseCountSingular => UiText.Get("StaffMembers.LicenseCountSingular", "{0} license");
        public static string LicenseCountPlural => UiText.Get("StaffMembers.LicenseCountPlural", "{0} licenses");
        public static string WorkingDaysCount => UiText.Get("StaffMembers.WorkingDaysCount", "Working days: {0}");
        public static string ActivityEmpty => UiText.Get("StaffMembers.ActivityEmpty", "No activity recorded yet");
        public static string ActivityEmptyHint => UiText.Get("StaffMembers.ActivityEmptyHint", "Audit history for this staff member will appear here as actions are taken.");
        public static string PortalAccessManageTitle => UiText.Get("StaffMembers.PortalAccessManageTitle", "Account management");
        public static string PortalAccessManageHint => UiText.Get("StaffMembers.PortalAccessManageHint", "Open the linked Identity account to manage MFA, sessions, status, and role assignments.");
        public static string OpenLinkedAccount => UiText.Get("StaffMembers.OpenLinkedAccount", "Open linked account");
        public static string PortalAccessLinkedTitle => UiText.Get("StaffMembers.PortalAccessLinkedTitle", "Portal account linked");
        public static string PortalAccessLinkedHint => UiText.Get("StaffMembers.PortalAccessLinkedHint", "This staff member can sign in to the portal with their linked account.");
        public static string PortalAccessUnlinkedTitle => UiText.Get("StaffMembers.PortalAccessUnlinkedTitle", "No portal account");
        public static string PortalAccessUnlinkedHint => UiText.Get("StaffMembers.PortalAccessUnlinkedHint", "Grant portal access to let this staff member sign in to the portal.");
        public static string PortalAccessUnavailableHint => UiText.Get("StaffMembers.PortalAccessUnavailableHint", "Portal access can be granted once the staff member is active.");
    }

    public static class Days
    {
        public static string Sunday => UiText.Get("Days.Sunday", "Sun");
        public static string Monday => UiText.Get("Days.Monday", "Mon");
        public static string Tuesday => UiText.Get("Days.Tuesday", "Tue");
        public static string Wednesday => UiText.Get("Days.Wednesday", "Wed");
        public static string Thursday => UiText.Get("Days.Thursday", "Thu");
        public static string Friday => UiText.Get("Days.Friday", "Fri");
        public static string Saturday => UiText.Get("Days.Saturday", "Sat");
    }

    public static class PortalAccess
    {
        public static string GrantTitle => UiText.Get("PortalAccess.GrantTitle", "Grant portal access");
        public static string GrantAction => UiText.Get("PortalAccess.GrantAction", "Grant portal access");
        public static string Role => UiText.Get("PortalAccess.Role", "Role");
        public static string RolePlaceholder => UiText.Get("PortalAccess.RolePlaceholder", "Select a compatible role");
        public static string RoleRequired => UiText.Get("PortalAccess.RoleRequired", "Select a role to continue.");
        public static string NoCompatibleRoles => UiText.Get("PortalAccess.NoCompatibleRoles", "No compatible role exists yet. Create one before granting access.");
        public static string Grant => UiText.Get("PortalAccess.Grant", "Send invitation");
        public static string Granted => UiText.Get("PortalAccess.Granted", "Portal access requested. An invitation will be sent shortly.");
        public static string HasAccess => UiText.Get("PortalAccess.HasAccess", "Portal account linked");
        public static string NoAccess => UiText.Get("PortalAccess.NoAccess", "No portal account");
        public static string Provisioning => UiText.Get("PortalAccess.Provisioning", "Provisioning");
        public static string Failed => UiText.Get("PortalAccess.Failed", "Provisioning failed");
        public static string Suspended => UiText.Get("PortalAccess.Suspended", "Suspended");
        public static string Explanation => UiText.Get("PortalAccess.Explanation", "An invited portal account will be created for this record using the selected role.");
        public static string GrantOnCreate => UiText.Get("PortalAccess.GrantOnCreate", "Grant portal access on creation");
        public static string GrantOnCreateHint => UiText.Get("PortalAccess.GrantOnCreateHint", "Send a portal invitation once this record is created.");
        public static string PendingLoginEmail => UiText.Get("PortalAccess.PendingLoginEmail", "Login email pending");
        public static string PendingLoginEmailShort => UiText.Get("PortalAccess.PendingLoginEmailShort", "Login verification pending");
        public static string PendingLoginEmailDetail => UiText.Get("PortalAccess.PendingLoginEmailDetail", "Waiting for {0} to confirm before the linked login email changes.");
        public static string LoginEmailChangeFailed => UiText.Get("PortalAccess.LoginEmailChangeFailed", "Login email change failed");
        public static string LoginEmailChangeFailedDetail => UiText.Get("PortalAccess.LoginEmailChangeFailedDetail", "Identity could not start verification: {0}");
    }

    public static class Errors
    {
        public static string Forbidden => UiText.Get("Errors.Forbidden", "You do not have permission to view this.");
        public static string NotFound => UiText.Get("Errors.NotFound", "The requested item was not found.");
        public static string LoadFailed => UiText.Get("Errors.LoadFailed", "We couldn't load this. Please try again.");
        public static string ReferenceLookupsForbidden => UiText.Get("Errors.ReferenceLookupsForbidden", "Your role does not include permission to load form lookups. Ask an administrator to add the \"View options\" reference permission to your role.");
    }
}
