namespace Operations.Application.Contracts;

/// <summary>The caller's station and AdHoc operation type are server-owned.</summary>
public sealed record SaveMobileAdHocFlightDetailsRequest(Guid CustomerId, string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc, DateTimeOffset ScheduledDepartureUtc);

public sealed record MobileAdHocFlightContextDto(Guid StationId, string StationIata, string StationName,
    string StationTimeZoneId, Guid CurrentStaffMemberId, bool CanCreate, bool CanSaveArrival,
    bool CanSaveDeparture, bool CanSaveServices, bool CanSaveTasks, IReadOnlyList<Guid> AllowedServiceIds,
    DateTimeOffset AsOfUtc, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc);
