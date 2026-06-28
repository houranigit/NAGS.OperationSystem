namespace MasterData.Api.Endpoints;

// Countries
public sealed record CreateCountryRequest(string Name, string IsoCode);
public sealed record UpdateCountryRequest(string Name, string IsoCode);

// ManpowerTypes
public sealed record CreateManpowerTypeRequest(string Name, string? Description);
public sealed record UpdateManpowerTypeRequest(string Name, string? Description);

// Licenses
public sealed record CreateLicenseRequest(string Code, string Name, string? Description);
public sealed record UpdateLicenseRequest(string Name, string? Description);

// Stations
public sealed record CreateStationRequest(
    string IataCode,
    string? IcaoCode,
    string Name,
    string City,
    Guid CountryId,
    IReadOnlyList<NewStationStaffRequest>? Staff);
public sealed record UpdateStationRequest(string IataCode, string? IcaoCode, string Name, string City, Guid CountryId);

public sealed record NewStationStaffRequest(
    string FullName,
    string EmployeeId,
    string Email,
    Guid ManpowerTypeId,
    EmploymentContractRequest? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    IReadOnlyList<StaffLicenseRequest>? Licenses,
    Guid? PortalAccessRoleId);

// Customers
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
    IReadOnlyList<CustomerContactRequest>? Contacts);

public sealed record UpdateCustomerRequest(
    string IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string? OfficialEmail,
    string? OfficialPhone,
    AddressRequest Address);

public sealed record AddCustomerContactRequest(string Name, string? JobTitle, string Email, string? Phone);
public sealed record UpdateCustomerContactRequest(string Name, string? JobTitle, string Email, string? Phone);

// Portal access
public sealed record GrantPortalAccessRequest(Guid RoleId);

// StaffMembers
public sealed record EmploymentContractRequest(DateOnly StartDate, DateOnly? EndDate);
public sealed record StaffLicenseRequest(Guid? Id, Guid LicenseId, string LicenseNumber);

public sealed record CreateStaffMemberRequest(
    string FullName,
    string EmployeeId,
    string Email,
    Guid StationId,
    Guid ManpowerTypeId,
    EmploymentContractRequest? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    IReadOnlyList<StaffLicenseRequest>? Licenses);

public sealed record UpdateStaffMemberRequest(
    string FullName,
    string EmployeeId,
    string Email,
    Guid StationId,
    Guid ManpowerTypeId,
    EmploymentContractRequest? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    IReadOnlyList<StaffLicenseRequest>? Licenses);
