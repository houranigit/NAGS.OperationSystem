namespace Operations.Application.Contracts;

public sealed record MobileFlightListItemDto(
    Guid Id,
    string FlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    string StationIataCode,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    bool IsPerLanding,
    string? AircraftTypeModel,
    Guid CustomerId,
    string? CustomerLogoVersion,
    Guid StationId,
    bool IsMyFlight,
    bool IsMyAdHocFlight,
    string StationTimeZoneId);

/// <summary>A page from the caller's station within the server's current rolling 24-hour window.</summary>
public sealed record MobileFlightPageDto(
    IReadOnlyList<MobileFlightListItemDto> Items,
    int Page,
    int PageSize,
    long TotalCount,
    Guid StationId,
    DateTimeOffset AsOfUtc,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc)
{
    public int TotalPages => PageSize <= 0 || TotalCount <= 0
        ? 0
        : (int)Math.Min(int.MaxValue, ((TotalCount - 1) / PageSize) + 1);

    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
