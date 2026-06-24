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
        public const string Portal = "";
        public const string Version = "v1.0.0";
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
        public const string MoreActions = "More actions";
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
        public const string Name = "Name";
        public const string None = "—";
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
        public const string Audit = "Audit log";
        public const string Account = "Account";
        public const string Overview = "Overview";
        public const string Administration = "Administration";
        public const string MasterData = "Master data";
        public const string Countries = "Countries";
        public const string ManpowerTypes = "Manpower types";
        public const string Licenses = "Licenses";
        public const string Stations = "Stations";
        public const string Customers = "Customers";
        public const string StaffMembers = "Staff members";
        public const string ToggleMenu = "Toggle navigation";
        public const string Language = "Language";
    }

    public static class Audit
    {
        public const string Title = "Audit log";
        public const string Description = "Permanent, append-only history of business and security changes.";
        public const string When = "When";
        public const string Actor = "Actor";
        public const string System = "System";
        public const string Module = "Module";
        public const string Entity = "Entity";
        public const string Action = "Action";
        public const string Empty = "No audit activity recorded yet.";
        public const string Changes = "Field changes";
        public const string Field = "Field";
        public const string Before = "Before";
        public const string After = "After";
        public const string NoChanges = "No field-level changes were recorded for this entry.";
        public const string ActivityTitle = "Recent activity";
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
        public const string InviteIntro = "Send an invitation so this person can set up their portal account.";
        public const string InviteAdminNote = "Direct user creation always creates a System Administrator with full access. Station staff and customer contacts are invited from their own records.";
        public const string InviteSuccess = "Invitation sent.";
        public const string InvitationDeliveryLabel = "Invitation delivery status";
        public const string EditTitle = "Edit user";
        public const string AssignRoleTitle = "Assign role";
        public const string ConfirmDeactivate = "Deactivate this user? They will be signed out of all sessions.";
        public const string ConfirmLock = "Lock this user out of signing in?";
        public const string StatActiveSessions = "Active sessions";
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
        public const string CompatibleUserType = "Account type";
        public const string System = "System";
        public const string PermissionCount = "Permissions";
        public const string UserCount = "Users";
        public const string RoleType = "Type";
        public const string Custom = "Custom";
        public const string NoDescription = "No description";
        public const string CreateTitle = "Create role";
        public const string EditTitle = "Edit role";
        public const string FormIntro = "Name the role and choose which account type it applies to.";
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

    public static class Countries
    {
        public const string Title = "Countries";
        public const string Description = "Maintain the ISO country list used across the system.";
        public const string Create = "New country";
        public const string Empty = "No countries match your filters.";
        public const string CountLabel = "countries";
        public const string SearchPlaceholder = "Search by name or code";
        public const string Name = "Name";
        public const string IsoCode = "ISO code";
        public const string Status = "Status";
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string AllStatuses = "All statuses";
        public const string Activate = "Activate";
        public const string Deactivate = "Deactivate";
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string CreateTitle = "Create country";
        public const string EditTitle = "Edit country";
        public const string FormIntro = "Maintain the country name and its ISO code.";
        public const string NameRequired = "A country name is required.";
        public const string IsoCodeRequired = "A 2-letter ISO code is required.";
        public const string SavedCreate = "Country created.";
        public const string SavedUpdate = "Country updated.";
        public const string Activated = "Country activated.";
        public const string Deactivated = "Country deactivated.";
        public const string ConfirmDeactivate = "Deactivate this country? It will no longer be selectable.";
    }

    public static class ManpowerTypes
    {
        public const string Title = "Manpower types";
        public const string Description = "Classify staff members by the work they perform.";
        public const string Create = "New manpower type";
        public const string Empty = "No manpower types match your filters.";
        public const string CountLabel = "manpower types";
        public const string SearchPlaceholder = "Search by name";
        public const string Name = "Name";
        public const string ManpowerDescription = "Description";
        public const string NoDescription = "No description";
        public const string Status = "Status";
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string AllStatuses = "All statuses";
        public const string Activate = "Activate";
        public const string Deactivate = "Deactivate";
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string CreateTitle = "Create manpower type";
        public const string EditTitle = "Edit manpower type";
        public const string FormIntro = "Name the manpower type and optionally describe it.";
        public const string NameRequired = "A name is required.";
        public const string SavedCreate = "Manpower type created.";
        public const string SavedUpdate = "Manpower type updated.";
        public const string Activated = "Manpower type activated.";
        public const string Deactivated = "Manpower type deactivated.";
        public const string ConfirmDeactivate = "Deactivate this manpower type? It will no longer be selectable.";
    }

    public static class Licenses
    {
        public const string Title = "Licenses";
        public const string Description = "Manage the licenses and certifications staff can hold.";
        public const string Create = "New license";
        public const string Empty = "No licenses match your filters.";
        public const string CountLabel = "licenses";
        public const string SearchPlaceholder = "Search by code or name";
        public const string Code = "Code";
        public const string Name = "Name";
        public const string LicenseDescription = "Description";
        public const string NoDescription = "No description";
        public const string Status = "Status";
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string AllStatuses = "All statuses";
        public const string Activate = "Activate";
        public const string Deactivate = "Deactivate";
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string CreateTitle = "Create license";
        public const string EditTitle = "Edit license";
        public const string FormIntro = "Define the license code, name and an optional description.";
        public const string CodeRequired = "A license code is required (2-10 letters or digits).";
        public const string NameRequired = "A license name is required.";
        public const string CodeImmutable = "The code cannot be changed after creation.";
        public const string SavedCreate = "License created.";
        public const string SavedUpdate = "License updated.";
        public const string Activated = "License activated.";
        public const string Deactivated = "License deactivated.";
        public const string ConfirmDeactivate = "Deactivate this license? It will no longer be selectable.";
    }

    public static class Stations
    {
        public const string Title = "Stations";
        public const string Description = "Manage the airport stations the operation works at.";
        public const string Create = "New station";
        public const string Empty = "No stations match your filters.";
        public const string CountLabel = "stations";
        public const string SearchPlaceholder = "Search by code, name or city";
        public const string IataCode = "IATA";
        public const string IcaoCode = "ICAO";
        public const string Name = "Name";
        public const string City = "City";
        public const string Country = "Country";
        public const string NoIcao = "—";
        public const string Status = "Status";
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string AllStatuses = "All statuses";
        public const string Activate = "Activate";
        public const string Deactivate = "Deactivate";
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string CreateTitle = "Create station";
        public const string EditTitle = "Edit station";
        public const string FormIntro = "Set the airport codes, name, city and country for this station.";
        public const string IataRequired = "A 3-letter IATA code is required.";
        public const string IcaoInvalid = "ICAO code must be exactly four letters.";
        public const string NameRequired = "A station name is required.";
        public const string CityRequired = "A city is required.";
        public const string CountryRequired = "An active country is required.";
        public const string SavedCreate = "Station created.";
        public const string SavedUpdate = "Station updated.";
        public const string Activated = "Station activated.";
        public const string Deactivated = "Station deactivated.";
        public const string ConfirmDeactivate = "Deactivate this station? Linked staff lose portal access.";
        public const string StaffMembers = "Staff members";

        // Creation wizard
        public const string StepDetails = "Station details";
        public const string StepStaff = "Add staff (optional)";
        public const string StepReview = "Review & create";
        public const string Next = "Next";
        public const string Previous = "Back";
        public const string AddStaff = "Add staff member";
        public const string RemoveStaff = "Remove";
        public const string StaffName = "Full name";
        public const string StaffEmail = "Email";
        public const string ManpowerType = "Manpower type";
        public const string GrantPortal = "Grant portal access";
        public const string PortalRole = "Portal role";
        public const string NoStaffYet = "No staff added. You can add staff now or later from the station page.";
        public const string ReviewStaffCount = "{0} staff member(s) will be created with this station.";
        public const string StaffIncomplete = "Each staff row needs a name, email, and manpower type.";
        public const string StaffRoleRequired = "Select a portal role or turn off portal access for this staff member.";
        public const string StaffMembersEmpty = "No staff members are assigned to this station yet.";
        public const string AddStaffMember = "Add staff member";
        public const string StatStaff = "Staff members";
        public const string StatActiveStaff = "Active staff";
        public const string StaffCountFormat = "{0} staff";
    }

    public static class Customers
    {
        public const string Title = "Customers";
        public const string Description = "Manage the airline customers the operation serves.";
        public const string Create = "New customer";
        public const string Empty = "No customers match your filters.";
        public const string CountLabel = "customers";
        public const string SearchPlaceholder = "Search by code or name";
        public const string IataCode = "IATA";
        public const string IcaoCode = "ICAO";
        public const string Name = "Name";
        public const string Country = "Country";
        public const string NoIcao = "—";
        public const string OfficialEmail = "Official email";
        public const string OfficialPhone = "Official phone";
        public const string Status = "Status";
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string AllStatuses = "All statuses";
        public const string Activate = "Activate";
        public const string Deactivate = "Deactivate";
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string CreateTitle = "Create customer";
        public const string EditTitle = "Edit customer";
        public const string FormIntro = "Capture the airline codes, official contact details, address and contacts.";
        public const string IataRequired = "A 2-character IATA code is required.";
        public const string IcaoInvalid = "ICAO code must be exactly three letters.";
        public const string NameRequired = "A customer name is required.";
        public const string CountryRequired = "An active country is required.";
        public const string SavedCreate = "Customer created.";
        public const string SavedUpdate = "Customer updated.";
        public const string Activated = "Customer activated.";
        public const string Deactivated = "Customer deactivated.";
        public const string ConfirmDeactivate = "Deactivate this customer? Linked contacts lose portal access.";

        public const string Address = "Official address";
        public const string AddressLine1 = "Address line 1";
        public const string AddressLine2 = "Address line 2";
        public const string City = "City";
        public const string Region = "State / region";
        public const string PostalCode = "Postal code";
        public const string AddressLine1Required = "Address line 1 is required.";
        public const string AddressCityRequired = "Address city is required.";

        public const string ContactsCount = "contacts";
        public const string Contacts = "Contacts";
        public const string ContactsEmpty = "No contacts have been added yet.";
        public const string AddContact = "Add contact";
        public const string EditContact = "Edit contact";
        public const string ContactName = "Name";
        public const string ContactJobTitle = "Job title";
        public const string ContactEmail = "Email";
        public const string ContactPhone = "Phone";
        public const string ContactNameRequired = "A contact name is required.";
        public const string ContactEmailRequired = "A valid contact email is required.";
        public const string ContactEmailDuplicate = "Contact emails must be unique within the customer.";
        public const string ContactRemove = "Remove";
        public const string ContactRemoved = "(will be removed)";
        public const string ContactAdded = "Contact added.";
        public const string PortalLinked = "Portal access";

        public const string TabAddress = "Address";
        public const string StatContacts = "Contacts";
        public const string StatPortalAccounts = "Portal accounts";
        public const string ContactsCountFormat = "{0} contacts";
    }

    public static class StaffMembers
    {
        public const string Title = "Staff members";
        public const string Description = "Manage station staff, their manpower type, schedule and licenses.";
        public const string Create = "New staff member";
        public const string Empty = "No staff members match your filters.";
        public const string CountLabel = "staff members";
        public const string SearchPlaceholder = "Search by name or email";
        public const string FullName = "Full name";
        public const string Email = "Email";
        public const string Station = "Station";
        public const string ManpowerType = "Manpower type";
        public const string Status = "Status";
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string AllStatuses = "All statuses";
        public const string Activate = "Activate";
        public const string Deactivate = "Deactivate";
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string None = "—";
        public const string CreateTitle = "Create staff member";
        public const string EditTitle = "Edit staff member";
        public const string BasicInformation = "Basic information";
        public const string FormIntro = "Update staff member information, schedule and licenses.";
        public const string ScheduleHint = "Select the days this staff member works.";
        public const string NameRequired = "A full name is required.";
        public const string EmailRequired = "A valid email is required.";
        public const string StationRequired = "An active station is required.";
        public const string ManpowerTypeRequired = "An active manpower type is required.";
        public const string SavedCreate = "Staff member created.";
        public const string SavedUpdate = "Staff member updated.";
        public const string Activated = "Staff member activated.";
        public const string Deactivated = "Staff member deactivated.";
        public const string ConfirmDeactivate = "Deactivate this staff member? A linked portal account loses access.";

        public const string EmploymentContract = "Employment contract";
        public const string StartDate = "Start date";
        public const string EndDate = "End date";
        public const string EndBeforeStart = "The end date cannot precede the start date.";
        public const string WorkingSchedule = "Working schedule";
        public const string NoSchedule = "No working schedule set.";
        public const string NoContract = "No employment contract.";

        public const string Licenses = "Licenses";
        public const string LicensesEmpty = "No licenses assigned.";
        public const string AddLicense = "Add license";
        public const string License = "License";
        public const string LicenseNumber = "License number";
        public const string LicenseRequired = "Select a license for each assignment.";
        public const string LicenseNumberRequired = "A license number is required for each assignment.";
        public const string LicenseDuplicate = "A staff member cannot hold the same license twice.";
        public const string PortalLinked = "Portal access";

        public const string EditMember = "Edit member";

        public const string TabOverview = "Overview";
        public const string TabLicenses = "Licenses";
        public const string TabSchedule = "Schedule";
        public const string TabPortalAccess = "Portal access";
        public const string TabActivity = "Activity";

        public const string PersonalInformation = "Personal information";
        public const string EmploymentDetails = "Employment details";
        public const string RecentActivity = "Recent activity";

        public const string StatLicenses = "Licenses";
        public const string StatWorkingDays = "Working days";
        public const string StatPortalAccount = "Portal account";
        public const string PortalLinkedShort = "Linked";
        public const string PortalNotLinkedShort = "Not created";

        public const string LicenseCountSingular = "{0} license";
        public const string LicenseCountPlural = "{0} licenses";
        public const string WorkingDaysCount = "Working days: {0}";

        public const string ActivityEmpty = "No activity recorded yet";
        public const string ActivityEmptyHint = "Audit history for this staff member will appear here as actions are taken.";

        public const string PortalAccessManageTitle = "Account management";
        public const string PortalAccessManageHint = "Future account controls — password resets, session management, and role changes — will be available here.";
        public const string PortalAccessLinkedTitle = "Portal account linked";
        public const string PortalAccessLinkedHint = "This staff member can sign in to the portal with their linked account.";
        public const string PortalAccessUnlinkedTitle = "No portal account";
        public const string PortalAccessUnlinkedHint = "Grant portal access to let this staff member sign in to the portal.";
        public const string PortalAccessUnavailableHint = "Portal access can be granted once the staff member is active.";
    }

    public static class Days
    {
        public const string Sunday = "Sun";
        public const string Monday = "Mon";
        public const string Tuesday = "Tue";
        public const string Wednesday = "Wed";
        public const string Thursday = "Thu";
        public const string Friday = "Fri";
        public const string Saturday = "Sat";
    }

    public static class PortalAccess
    {
        public const string GrantTitle = "Grant portal access";
        public const string GrantAction = "Grant portal access";
        public const string Role = "Role";
        public const string RolePlaceholder = "Select a compatible role";
        public const string RoleRequired = "Select a role to continue.";
        public const string NoCompatibleRoles = "No compatible role exists yet. Create one before granting access.";
        public const string Grant = "Send invitation";
        public const string Granted = "Portal access requested. An invitation will be sent shortly.";
        public const string HasAccess = "Portal account linked";
        public const string NoAccess = "No portal account";
        public const string Explanation = "An invited portal account will be created for this record using the selected role.";
    }

    public static class Errors
    {
        public const string Forbidden = "You do not have permission to view this.";
        public const string NotFound = "The requested item was not found.";
        public const string LoadFailed = "We couldn't load this. Please try again.";
    }
}
