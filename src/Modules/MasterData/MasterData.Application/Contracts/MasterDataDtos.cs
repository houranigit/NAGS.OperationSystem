namespace MasterData.Application.Contracts;

// --- Countries -------------------------------------------------------------

public sealed record CountryListItemDto(
    Guid Id,
    string Name,
    string IsoCode,
    bool IsActive);

public sealed record CountryDto(
    Guid Id,
    string Name,
    string IsoCode,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

/// <summary>A lightweight active-country option for pickers (e.g. Station/Customer forms).</summary>
public sealed record CountryOptionDto(Guid Id, string Name, string IsoCode);

// --- ManpowerTypes ---------------------------------------------------------

public sealed record ManpowerTypeListItemDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive);

public sealed record ManpowerTypeDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record ManpowerTypeOptionDto(Guid Id, string Name);

// --- Licenses --------------------------------------------------------------

public sealed record LicenseListItemDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive);

public sealed record LicenseDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record LicenseOptionDto(Guid Id, string Code, string Name);

// --- Stations --------------------------------------------------------------

public sealed record StationListItemDto(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string? City,
    Guid CountryId,
    string CountryName,
    bool IsActive);

public sealed record StationDto(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string? City,
    Guid CountryId,
    string CountryName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record StationOptionDto(Guid Id, string IataCode, string Name);

// --- Customers -------------------------------------------------------------

public sealed record CustomerListItemDto(
    Guid Id,
    string? IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string CountryName,
    bool IsActive,
    int ContactCount);

public sealed record AddressDto(
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode);

public sealed record CustomerContactDto(
    Guid Id,
    string Name,
    string? JobTitle,
    string Email,
    string? Phone,
    Guid? LinkedUserId,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CustomerDto(
    Guid Id,
    string? IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string CountryName,
    string? OfficialEmail,
    string? OfficialPhone,
    string? LogoFileReference,
    AddressDto Address,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion,
    IReadOnlyList<CustomerContactDto> Contacts);

public sealed record CustomerOptionDto(Guid Id, string? IataCode, string Name);

// --- StaffMembers ----------------------------------------------------------

public sealed record StaffMemberListItemDto(
    Guid Id,
    string FullName,
    string EmployeeId,
    string Email,
    Guid StationId,
    string StationCode,
    Guid ManpowerTypeId,
    string ManpowerTypeName,
    bool IsActive);

public sealed record EmploymentContractDto(DateOnly StartDate, DateOnly? EndDate);

public sealed record StaffMemberLicenseDto(Guid Id, Guid LicenseId, string LicenseCode, string LicenseName, string LicenseNumber);

public sealed record StaffMemberDto(
    Guid Id,
    string FullName,
    string EmployeeId,
    string Email,
    Guid StationId,
    string StationCode,
    string StationName,
    Guid ManpowerTypeId,
    string ManpowerTypeName,
    EmploymentContractDto? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    Guid? LinkedUserId,
    string PortalState,
    string? PortalFailureReason,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion,
    IReadOnlyList<StaffMemberLicenseDto> Licenses);
