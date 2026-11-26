using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;

namespace Operations.Application.Features.Flights;

public sealed record GetFlightCalendarQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    Guid? StationId = null,
    Guid? CustomerId = null) : IQuery<IReadOnlyList<CalendarFlightDto>>;

public sealed class GetFlightCalendarQueryHandler(
    IOperationsDbContext db, IOperationsScope scope, IMasterDataReader masterData, TimeProvider clock)
    : IQueryHandler<GetFlightCalendarQuery, IReadOnlyList<CalendarFlightDto>>
{
    public async Task<Result<IReadOnlyList<CalendarFlightDto>>> Handle(GetFlightCalendarQuery request, CancellationToken ct)
    {
        var scopeResult = await scope.ResolveAsync(ct);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        if (request.ToUtc <= request.FromUtc || request.ToUtc - request.FromUtc > TimeSpan.FromDays(62))
            return Error.Validation("The calendar date range must be greater than zero and no longer than 62 days.",
                "Operations.Flight.CalendarRangeInvalid");

        var flights = FlightListQuery.ApplyScopeAndFilters(db.Flights.AsNoTracking(), scopeResult.Value,
                new FlightListFilter(null, request.StationId, request.CustomerId, null, request.FromUtc, null))
            .Where(flight => flight.Schedule.Sta < request.ToUtc)
            .OrderBy(flight => flight.Schedule.Sta)
            .ThenBy(flight => flight.Id);

        var items = await FlightListQuery.MatchReportFilters(flights, FlightListType.All, null, clock.GetUtcNow(), null, null)
            .Select(item => new
            {
                item.Flight.Id,
                FlightNumber = item.Flight.FlightNumber.Value,
                CustomerName = item.Flight.Customer.Name,
                StationIata = item.Flight.Station.IataCode,
                StationName = item.Flight.Station.Name,
                item.Flight.Station.StationId,
                item.Status,
                item.IsAdHocFlight,
                item.IsPerLanding,
                item.IsOnCall,
                ScheduledArrivalUtc = item.Flight.Schedule.Sta,
                ScheduledDepartureUtc = item.Flight.Schedule.Std,
                item.Flight.ApprovalStatus,
                item.ActualArrivalUtc,
                item.ActualDepartureUtc
            })
            .ToListAsync(ct);

        if (items.Count == 0)
            return Array.Empty<CalendarFlightDto>();

        var stations = await masterData.GetStationsAsync(items.Select(item => item.StationId).Distinct().ToArray(), ct);
        var stationTimeZones = stations.ToDictionary(station => station.Id, station => station.TimeZoneId);
        if (items.Any(item => !stationTimeZones.TryGetValue(item.StationId, out var zone) || string.IsNullOrWhiteSpace(zone)))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");

        return items.Select(item => new CalendarFlightDto(
            item.Id, item.FlightNumber, item.CustomerName, item.StationIata, item.StationName, stationTimeZones[item.StationId],
            item.Status.ToString(), item.IsAdHocFlight, item.IsPerLanding, item.IsOnCall, item.ScheduledArrivalUtc,
            item.ScheduledDepartureUtc, item.ApprovalStatus, item.ActualArrivalUtc, item.ActualDepartureUtc)).ToArray();
    }
}
