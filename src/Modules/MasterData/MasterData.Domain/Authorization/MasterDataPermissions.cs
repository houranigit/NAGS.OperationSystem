namespace MasterData.Domain.Authorization;

/// <summary>
/// MasterData permission catalog. Codes follow the lowercase <c>masterdata.resource.action</c>
/// convention. UserType compatibility is declared in the application-layer permission catalog.
/// </summary>
public static class MasterDataPermissions
{
    /// <summary>
    /// Read-only access to active reference lookups (country/manpower-type/license options) so
    /// scoped users such as Station Staff can populate station and staff forms without being granted
    /// management access to those catalogs, which remains administrator-only.
    /// </summary>
    public static class Reference
    {
        public const string ViewOptions = "masterdata.reference.view-options";
    }

    public static class Countries
    {
        public const string View = "masterdata.countries.view";
        public const string Create = "masterdata.countries.create";
        public const string Update = "masterdata.countries.update";
        public const string Activate = "masterdata.countries.activate";
        public const string Deactivate = "masterdata.countries.deactivate";
    }

    public static class ManpowerTypes
    {
        public const string View = "masterdata.manpower-types.view";
        public const string Create = "masterdata.manpower-types.create";
        public const string Update = "masterdata.manpower-types.update";
        public const string Activate = "masterdata.manpower-types.activate";
        public const string Deactivate = "masterdata.manpower-types.deactivate";
    }

    public static class Licenses
    {
        public const string View = "masterdata.licenses.view";
        public const string Create = "masterdata.licenses.create";
        public const string Update = "masterdata.licenses.update";
        public const string Activate = "masterdata.licenses.activate";
        public const string Deactivate = "masterdata.licenses.deactivate";
    }

    public static class Stations
    {
        public const string View = "masterdata.stations.view";
        public const string Create = "masterdata.stations.create";
        public const string Update = "masterdata.stations.update";
        public const string Activate = "masterdata.stations.activate";
        public const string Deactivate = "masterdata.stations.deactivate";
    }

    public static class StaffMembers
    {
        public const string View = "masterdata.staff-members.view";
        public const string Create = "masterdata.staff-members.create";
        public const string Update = "masterdata.staff-members.update";
        public const string Activate = "masterdata.staff-members.activate";
        public const string Deactivate = "masterdata.staff-members.deactivate";
        public const string GrantAccess = "masterdata.staff-members.grant-access";
    }

    public static class Customers
    {
        public const string View = "masterdata.customers.view";
        public const string Create = "masterdata.customers.create";
        public const string Update = "masterdata.customers.update";
        public const string Activate = "masterdata.customers.activate";
        public const string Deactivate = "masterdata.customers.deactivate";
    }

    public static class CustomerContacts
    {
        public const string View = "masterdata.customer-contacts.view";
        public const string Create = "masterdata.customer-contacts.create";
        public const string Update = "masterdata.customer-contacts.update";
        public const string Remove = "masterdata.customer-contacts.remove";
        public const string GrantAccess = "masterdata.customer-contacts.grant-access";
    }
}
