namespace MasterData.Domain.Authorization;

/// <summary>
/// MasterData permission catalog. Codes follow the lowercase <c>masterdata.resource.action</c>
/// convention. UserType compatibility is declared in the application-layer permission catalog.
/// </summary>
public static class MasterDataPermissions
{
    /// <summary>
    /// Read-only access to active reference lookups so scoped users such as Station Staff can
    /// populate station, staff, and flight forms without being granted management access to those
    /// catalogs, which remains administrator-only.
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

    public static class Services
    {
        public const string View = "masterdata.services.view";
        public const string Create = "masterdata.services.create";
        public const string Update = "masterdata.services.update";
        public const string Activate = "masterdata.services.activate";
        public const string Deactivate = "masterdata.services.deactivate";
    }

    public static class OperationTypes
    {
        public const string View = "masterdata.operation-types.view";
        public const string Create = "masterdata.operation-types.create";
        public const string Update = "masterdata.operation-types.update";
        public const string Activate = "masterdata.operation-types.activate";
        public const string Deactivate = "masterdata.operation-types.deactivate";
    }

    public static class AircraftTypes
    {
        public const string View = "masterdata.aircraft-types.view";
        public const string Create = "masterdata.aircraft-types.create";
        public const string Update = "masterdata.aircraft-types.update";
        public const string Activate = "masterdata.aircraft-types.activate";
        public const string Deactivate = "masterdata.aircraft-types.deactivate";
    }

    public static class Tools
    {
        public const string View = "masterdata.tools.view";
        public const string Create = "masterdata.tools.create";
        public const string Update = "masterdata.tools.update";
        public const string Activate = "masterdata.tools.activate";
        public const string Deactivate = "masterdata.tools.deactivate";
    }

    public static class Materials
    {
        public const string View = "masterdata.materials.view";
        public const string Create = "masterdata.materials.create";
        public const string Update = "masterdata.materials.update";
        public const string Activate = "masterdata.materials.activate";
        public const string Deactivate = "masterdata.materials.deactivate";
    }

    public static class GeneralSupports
    {
        public const string View = "masterdata.general-supports.view";
        public const string Create = "masterdata.general-supports.create";
        public const string Update = "masterdata.general-supports.update";
        public const string Activate = "masterdata.general-supports.activate";
        public const string Deactivate = "masterdata.general-supports.deactivate";
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
