using System.Globalization;

namespace OperationsSystem.Blazor.Client.Api;

/// <summary>
/// Typed access to the MasterData module API (<c>/api/v1/masterdata</c>). Editable records use
/// optimistic concurrency: reads return a <c>RowVersion</c> that mutations echo back as <c>If-Match</c>.
/// </summary>
public sealed class MasterDataApiClient(BrowserApiClient api)
{
    // --- Countries ---------------------------------------------------------

    public Task<PagedResult<CountryListItem>> GetCountriesAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page)
            .Add("pageSize", pageSize)
            .Add("search", search)
            .Add("isActive", isActive)
            .Add("sort", sort)
            .Build();
        return api.GetAsync<PagedResult<CountryListItem>>($"/masterdata/countries{query}", ct);
    }

    public Task<IReadOnlyList<CountryOption>> GetCountryOptionsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<CountryOption>>("/masterdata/countries/options", ct);

    public Task<CountryDetail> GetCountryAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<CountryDetail>($"/masterdata/countries/{id}", ct);

    public Task<Guid> CreateCountryAsync(CreateCountryRequest request, CancellationToken ct = default) =>
        api.PostAsync<CreateCountryRequest, Guid>("/masterdata/countries", request, ct);

    public Task UpdateCountryAsync(Guid id, UpdateCountryRequest request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/masterdata/countries/{id}", request, rowVersion, ct);

    public Task ActivateCountryAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/countries/{id}/activate", rowVersion, ct);

    public Task DeactivateCountryAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/countries/{id}/deactivate", rowVersion, ct);

    // --- ManpowerTypes -----------------------------------------------------

    public Task<PagedResult<ManpowerTypeListItem>> GetManpowerTypesAsync(
        int page, int pageSize, string? search, bool? isActive = null, string? sort = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page).Add("pageSize", pageSize).Add("search", search)
            .Add("isActive", isActive).Add("sort", sort).Build();
        return api.GetAsync<PagedResult<ManpowerTypeListItem>>($"/masterdata/manpower-types{query}", ct);
    }

    public Task<IReadOnlyList<ManpowerTypeOption>> GetManpowerTypeOptionsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<ManpowerTypeOption>>("/masterdata/manpower-types/options", ct);

    public Task<ManpowerTypeDetail> GetManpowerTypeAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<ManpowerTypeDetail>($"/masterdata/manpower-types/{id}", ct);

    public Task<Guid> CreateManpowerTypeAsync(CreateManpowerTypeRequest request, CancellationToken ct = default) =>
        api.PostAsync<CreateManpowerTypeRequest, Guid>("/masterdata/manpower-types", request, ct);

    public Task UpdateManpowerTypeAsync(Guid id, UpdateManpowerTypeRequest request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/masterdata/manpower-types/{id}", request, rowVersion, ct);

    public Task ActivateManpowerTypeAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/manpower-types/{id}/activate", rowVersion, ct);

    public Task DeactivateManpowerTypeAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/manpower-types/{id}/deactivate", rowVersion, ct);

    // --- Licenses ----------------------------------------------------------

    public Task<PagedResult<LicenseListItem>> GetLicensesAsync(
        int page, int pageSize, string? search, bool? isActive = null, string? sort = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page).Add("pageSize", pageSize).Add("search", search)
            .Add("isActive", isActive).Add("sort", sort).Build();
        return api.GetAsync<PagedResult<LicenseListItem>>($"/masterdata/licenses{query}", ct);
    }

    public Task<IReadOnlyList<LicenseOption>> GetLicenseOptionsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<LicenseOption>>("/masterdata/licenses/options", ct);

    public Task<LicenseDetail> GetLicenseAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<LicenseDetail>($"/masterdata/licenses/{id}", ct);

    public Task<Guid> CreateLicenseAsync(CreateLicenseRequest request, CancellationToken ct = default) =>
        api.PostAsync<CreateLicenseRequest, Guid>("/masterdata/licenses", request, ct);

    public Task UpdateLicenseAsync(Guid id, UpdateLicenseRequest request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/masterdata/licenses/{id}", request, rowVersion, ct);

    public Task ActivateLicenseAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/licenses/{id}/activate", rowVersion, ct);

    public Task DeactivateLicenseAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/licenses/{id}/deactivate", rowVersion, ct);

    // --- Stations ----------------------------------------------------------

    public Task<PagedResult<StationListItem>> GetStationsAsync(
        int page, int pageSize, string? search, bool? isActive = null, Guid? countryId = null, string? sort = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page).Add("pageSize", pageSize).Add("search", search)
            .Add("isActive", isActive).Add("countryId", countryId?.ToString()).Add("sort", sort).Build();
        return api.GetAsync<PagedResult<StationListItem>>($"/masterdata/stations{query}", ct);
    }

    public Task<IReadOnlyList<StationOption>> GetStationOptionsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<StationOption>>("/masterdata/stations/options", ct);

    public Task<StationDetail> GetStationAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<StationDetail>($"/masterdata/stations/{id}", ct);

    public Task<Guid> CreateStationAsync(CreateStationRequest request, CancellationToken ct = default) =>
        api.PostAsync<CreateStationRequest, Guid>("/masterdata/stations", request, ct);

    public Task UpdateStationAsync(Guid id, UpdateStationRequest request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/masterdata/stations/{id}", request, rowVersion, ct);

    public Task ActivateStationAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/stations/{id}/activate", rowVersion, ct);

    public Task DeactivateStationAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/stations/{id}/deactivate", rowVersion, ct);

    // --- Customers ---------------------------------------------------------

    public Task<PagedResult<CustomerListItem>> GetCustomersAsync(
        int page, int pageSize, string? search, bool? isActive = null, Guid? countryId = null, string? sort = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page).Add("pageSize", pageSize).Add("search", search)
            .Add("isActive", isActive).Add("countryId", countryId?.ToString()).Add("sort", sort).Build();
        return api.GetAsync<PagedResult<CustomerListItem>>($"/masterdata/customers{query}", ct);
    }

    public Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<CustomerOption>>("/masterdata/customers/options", ct);

    public Task<CustomerDetail> GetCustomerAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<CustomerDetail>($"/masterdata/customers/{id}", ct);

    public Task<Guid> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken ct = default) =>
        api.PostAsync<CreateCustomerRequest, Guid>("/masterdata/customers", request, ct);

    public Task UpdateCustomerAsync(Guid id, UpdateCustomerRequest request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/masterdata/customers/{id}", request, rowVersion, ct);

    public Task UploadCustomerLogoAsync(Guid id, byte[] content, string fileName, string contentType, string rowVersion, CancellationToken ct = default) =>
        api.UploadFileAsync($"/masterdata/customers/{id}/logo", content, fileName, contentType, rowVersion, ct);

    public Task<BrowserFileContent> GetCustomerLogoAsync(Guid id, CancellationToken ct = default) =>
        api.GetFileAsync($"/masterdata/customers/{id}/logo", ct);

    public Task<Guid> AddCustomerContactAsync(Guid id, AddCustomerContactRequest request, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync<AddCustomerContactRequest, Guid>($"/masterdata/customers/{id}/contacts", request, rowVersion, ct);

    public Task ActivateCustomerAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/customers/{id}/activate", rowVersion, ct);

    public Task DeactivateCustomerAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/customers/{id}/deactivate", rowVersion, ct);

    // --- Staff members -----------------------------------------------------

    public Task<PagedResult<StaffMemberListItem>> GetStaffMembersAsync(
        int page, int pageSize, string? search, bool? isActive = null, Guid? stationId = null, Guid? manpowerTypeId = null, string? sort = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page).Add("pageSize", pageSize).Add("search", search)
            .Add("isActive", isActive).Add("stationId", stationId?.ToString()).Add("manpowerTypeId", manpowerTypeId?.ToString()).Add("sort", sort).Build();
        return api.GetAsync<PagedResult<StaffMemberListItem>>($"/masterdata/staff-members{query}", ct);
    }

    public Task<StaffMemberDetail> GetStaffMemberAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<StaffMemberDetail>($"/masterdata/staff-members/{id}", ct);

    public Task<Guid> CreateStaffMemberAsync(CreateStaffMemberRequest request, CancellationToken ct = default) =>
        api.PostAsync<CreateStaffMemberRequest, Guid>("/masterdata/staff-members", request, ct);

    public Task UpdateStaffMemberAsync(Guid id, UpdateStaffMemberRequest request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/masterdata/staff-members/{id}", request, rowVersion, ct);

    public Task ActivateStaffMemberAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/staff-members/{id}/activate", rowVersion, ct);

    public Task DeactivateStaffMemberAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/staff-members/{id}/deactivate", rowVersion, ct);

    // --- Portal access -----------------------------------------------------

    public Task GrantStaffPortalAccessAsync(Guid staffMemberId, GrantPortalAccessRequest request, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/staff-members/{staffMemberId}/grant-access", request, ct);

    public Task GrantContactPortalAccessAsync(Guid customerId, Guid contactId, GrantPortalAccessRequest request, CancellationToken ct = default) =>
        api.PostAsync($"/masterdata/customers/{customerId}/contacts/{contactId}/grant-access", request, ct);

    private sealed class QueryBuilder
    {
        private readonly List<string> _parts = [];

        public QueryBuilder Add(string key, int value)
        {
            _parts.Add($"{key}={value.ToString(CultureInfo.InvariantCulture)}");
            return this;
        }

        public QueryBuilder Add(string key, bool? value)
        {
            if (value is { } v)
                _parts.Add($"{key}={v.ToString().ToLowerInvariant()}");
            return this;
        }

        public QueryBuilder Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                _parts.Add($"{key}={Uri.EscapeDataString(value)}");
            return this;
        }

        public string Build() => _parts.Count == 0 ? string.Empty : "?" + string.Join('&', _parts);
    }
}
