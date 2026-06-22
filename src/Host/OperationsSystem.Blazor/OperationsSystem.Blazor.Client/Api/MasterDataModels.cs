namespace OperationsSystem.Blazor.Client.Api;

// --- Countries -------------------------------------------------------------

public sealed record CountryListItem(
    Guid Id,
    string Name,
    string IsoCode,
    bool IsActive);

public sealed record CountryDetail(
    Guid Id,
    string Name,
    string IsoCode,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record CountryOption(Guid Id, string Name, string IsoCode);

public sealed record CreateCountryRequest(string Name, string IsoCode);
public sealed record UpdateCountryRequest(string Name, string IsoCode);

// --- ManpowerTypes ---------------------------------------------------------

public sealed record ManpowerTypeListItem(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive);

public sealed record ManpowerTypeDetail(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record ManpowerTypeOption(Guid Id, string Name);

public sealed record CreateManpowerTypeRequest(string Name, string? Description);
public sealed record UpdateManpowerTypeRequest(string Name, string? Description);

// --- Licenses --------------------------------------------------------------

public sealed record LicenseListItem(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive);

public sealed record LicenseDetail(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record LicenseOption(Guid Id, string Code, string Name);

public sealed record CreateLicenseRequest(string Code, string Name, string? Description);
public sealed record UpdateLicenseRequest(string Name, string? Description);

// --- Stations --------------------------------------------------------------

public sealed record StationListItem(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string City,
    Guid CountryId,
    string CountryName,
    bool IsActive);

public sealed record StationDetail(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string City,
    Guid CountryId,
    string CountryName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record StationOption(Guid Id, string IataCode, string Name);

public sealed record CreateStationRequest(string IataCode, string? IcaoCode, string Name, string City, Guid CountryId);
public sealed record UpdateStationRequest(string IataCode, string? IcaoCode, string Name, string City, Guid CountryId);

// --- Customers -------------------------------------------------------------

public sealed record CustomerListItem(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string CountryName,
    bool IsActive,
    int ContactCount);

public sealed record AddressModel(string Line1, string? Line2, string City, string? Region, string? PostalCode);

public sealed record CustomerContactModel(
    Guid Id,
    string Name,
    string? JobTitle,
    string Email,
    string? Phone,
    Guid? LinkedUserId,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CustomerDetail(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string CountryName,
    string? OfficialEmail,
    string? OfficialPhone,
    string? LogoFileReference,
    AddressModel Address,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion,
    IReadOnlyList<CustomerContactModel> Contacts);

public sealed record CustomerOption(Guid Id, string IataCode, string Name);

public sealed record AddressRequest(string Line1, string? Line2, string City, string? Region, string? PostalCode);
public sealed record CustomerContactRequest(Guid? Id, string Name, string? JobTitle, string Email, string? Phone);

public sealed record CreateCustomerRequest(
    string IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string? OfficialEmail,
    string? OfficialPhone,
    AddressRequest Address,
    IReadOnlyList<CustomerContactRequest> Contacts);

public sealed record UpdateCustomerRequest(
    string IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string? OfficialEmail,
    string? OfficialPhone,
    AddressRequest Address,
    IReadOnlyList<CustomerContactRequest> Contacts);

public sealed record AddCustomerContactRequest(string Name, string? JobTitle, string Email, string? Phone);

// --- StaffMembers ----------------------------------------------------------

public sealed record StaffMemberListItem(
    Guid Id,
    string FullName,
    string Email,
    Guid StationId,
    string StationCode,
    Guid ManpowerTypeId,
    string ManpowerTypeName,
    bool IsActive);

public sealed record EmploymentContractModel(DateOnly StartDate, DateOnly? EndDate);

public sealed record StaffMemberLicenseModel(Guid Id, Guid LicenseId, string LicenseCode, string LicenseName, string LicenseNumber);

public sealed record StaffMemberDetail(
    Guid Id,
    string FullName,
    string Email,
    Guid StationId,
    string StationCode,
    string StationName,
    Guid ManpowerTypeId,
    string ManpowerTypeName,
    EmploymentContractModel? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    Guid? LinkedUserId,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion,
    IReadOnlyList<StaffMemberLicenseModel> Licenses);

public sealed record EmploymentContractRequest(DateOnly StartDate, DateOnly? EndDate);
public sealed record StaffLicenseRequest(Guid? Id, Guid LicenseId, string LicenseNumber);

public sealed record CreateStaffMemberRequest(
    string FullName,
    string Email,
    Guid StationId,
    Guid ManpowerTypeId,
    EmploymentContractRequest? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    IReadOnlyList<StaffLicenseRequest> Licenses);

public sealed record UpdateStaffMemberRequest(
    string FullName,
    string Email,
    Guid StationId,
    Guid ManpowerTypeId,
    EmploymentContractRequest? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    IReadOnlyList<StaffLicenseRequest> Licenses);

// --- Portal access ---------------------------------------------------------

/// <summary>Requests an invited portal account for a StaffMember/CustomerContact using a compatible role.</summary>
public sealed record GrantPortalAccessRequest(Guid RoleId);
