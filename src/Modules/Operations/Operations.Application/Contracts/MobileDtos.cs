namespace Operations.Application.Contracts;

// Wire shapes for the dedicated mobile surface (/api/v1/mobile). The mobile client mirrors
// these into its local offline cache, so they are stable public contracts: renames or removals
// require a coordinated mobile release.

/// <summary>The calling staff member's profile, resolved from the JWT's external reference.</summary>
public sealed record MobileMeDto(
    Guid StaffMemberId,
    string FullName,
    string EmployeeId,
    Guid StationId,
    string StationIata,
    string StationName,
    Guid ManpowerTypeId,
    string? ManpowerTypeName);

public sealed record MobileCatalogItemDto(Guid Id, string Name);

/// <summary>
/// A service catalog row. <see cref="IsAircraftPerLanding"/> marks the well-known Aircraft Per
/// Landing service, which the client must exclude from work-order service-line pickers (it is a
/// flight designation, not a performable service).
/// </summary>
public sealed record MobileServiceCatalogItemDto(Guid Id, string Name, bool IsAircraftPerLanding);

public sealed record MobileCustomerDto(Guid Id, string? IataCode, string Name);

public sealed record MobileAircraftTypeDto(Guid Id, string Manufacturer, string Model);

/// <summary>All shared catalogs in one payload so the client refreshes its cache with a single call.</summary>
public sealed record MobileCatalogsDto(
    IReadOnlyList<MobileServiceCatalogItemDto> Services,
    IReadOnlyList<MobileCatalogItemDto> Tools,
    IReadOnlyList<MobileCatalogItemDto> Materials,
    IReadOnlyList<MobileCatalogItemDto> GeneralSupports,
    IReadOnlyList<MobileCustomerDto> Customers,
    IReadOnlyList<MobileAircraftTypeDto> AircraftTypes,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// One flight row for the mobile cache. A single shape serves the My/Per-Landing/Ad-Hoc lists and
/// the single-flight fetch used by the realtime upsert path, so the client has one apply path.
/// <see cref="MyWorkOrder"/> embeds the caller's active work order (full detail, for offline form
/// hydration); <see cref="OtherWorkOrdersExist"/> signals that another user already opened one.
/// The mobile-window fields let an unrestricted by-id notification deep link remain readable while
/// clients keep actions disabled and avoid admitting the row into their list cache.
/// </summary>
public sealed record MobileFlightDto(
    Guid Id,
    string FlightNumber,
    string OriginalFlightNumber,
    Guid CustomerId,
    string? CustomerIataCode,
    string CustomerName,
    Guid StationId,
    string StationIata,
    Guid OperationTypeId,
    string OperationTypeName,
    Guid? AircraftTypeId,
    string? AircraftTypeModel,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string Status,
    bool IsPerLanding,
    bool IsAdHoc,
    IReadOnlyList<PlannedServiceDto> PlannedServices,
    IReadOnlyList<AssignedEmployeeDto> AssignedEmployees,
    WorkOrderDetailDto? MyWorkOrder,
    bool OtherWorkOrdersExist,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion,
    bool IsWithinMobileWindow,
    DateTimeOffset MobileWindowStartsAtUtc,
    DateTimeOffset MobileWindowEndsAtUtc);
