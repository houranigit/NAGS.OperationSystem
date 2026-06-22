namespace OperationsSystem.Blazor.Client.Auth;

/// <summary>
/// Client-side mirror of the MasterData module permission names (the backend stays authoritative).
/// Used only to gate UI; the API enforces these on every request.
/// </summary>
public static class MasterDataPermissions
{
    public const string CountriesView = "masterdata.countries.view";
    public const string CountriesCreate = "masterdata.countries.create";
    public const string CountriesUpdate = "masterdata.countries.update";
    public const string CountriesActivate = "masterdata.countries.activate";
    public const string CountriesDeactivate = "masterdata.countries.deactivate";

    public const string ManpowerTypesView = "masterdata.manpower-types.view";
    public const string ManpowerTypesCreate = "masterdata.manpower-types.create";
    public const string ManpowerTypesUpdate = "masterdata.manpower-types.update";
    public const string ManpowerTypesActivate = "masterdata.manpower-types.activate";
    public const string ManpowerTypesDeactivate = "masterdata.manpower-types.deactivate";

    public const string LicensesView = "masterdata.licenses.view";
    public const string LicensesCreate = "masterdata.licenses.create";
    public const string LicensesUpdate = "masterdata.licenses.update";
    public const string LicensesActivate = "masterdata.licenses.activate";
    public const string LicensesDeactivate = "masterdata.licenses.deactivate";

    public const string StationsView = "masterdata.stations.view";
    public const string StationsCreate = "masterdata.stations.create";
    public const string StationsUpdate = "masterdata.stations.update";
    public const string StationsActivate = "masterdata.stations.activate";
    public const string StationsDeactivate = "masterdata.stations.deactivate";

    public const string CustomersView = "masterdata.customers.view";
    public const string CustomersCreate = "masterdata.customers.create";
    public const string CustomersUpdate = "masterdata.customers.update";
    public const string CustomersActivate = "masterdata.customers.activate";
    public const string CustomersDeactivate = "masterdata.customers.deactivate";

    public const string CustomerContactsCreate = "masterdata.customer-contacts.create";
    public const string CustomerContactsUpdate = "masterdata.customer-contacts.update";
    public const string CustomerContactsRemove = "masterdata.customer-contacts.remove";
    public const string CustomerContactsGrantAccess = "masterdata.customer-contacts.grant-access";

    public const string StaffMembersView = "masterdata.staff-members.view";
    public const string StaffMembersCreate = "masterdata.staff-members.create";
    public const string StaffMembersUpdate = "masterdata.staff-members.update";
    public const string StaffMembersActivate = "masterdata.staff-members.activate";
    public const string StaffMembersDeactivate = "masterdata.staff-members.deactivate";
    public const string StaffMembersGrantAccess = "masterdata.staff-members.grant-access";
}
