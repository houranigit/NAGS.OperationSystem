using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;

namespace Operations.Application.Features.Flights;

public sealed record GetMobileFlightHistoryQuery(int Page = 1, int PageSize = 20, bool UsedSearchOnly = false, string? Search = null)
    : IQuery<MobileFlightPageDto>;

public sealed class GetMobileFlightHistoryQueryHandler(IOperationsDbContext db, IMobileFlightScope scope,
    ICustomerLogoReader logos, IMasterDataReader masterData, IUserDisplayNameReader? userNames = null)
    : IQueryHandler<GetMobileFlightHistoryQuery, MobileFlightPageDto>
{
    public async Task<Result<MobileFlightPageDto>> Handle(GetMobileFlightHistoryQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var paging = PageRequest.From(request.Page, request.PageSize);
        var matchingFlights = FlightListQuery.ApplySearch(db.Flights.AsNoTracking(), request.Search);
        var query = from activity in db.FlightUserActivities.AsNoTracking()
            join flight in matchingFlights on activity.FlightId equals flight.Id
            where activity.UserId == actor.UserId && flight.Station.StationId == actor.StationId &&
                flight.Schedule.Sta <= actor.WindowEndUtc && flight.Schedule.Std >= actor.WindowStartUtc
            select new { Flight = flight, activity.LastInteractionAtUtc, activity.UsedSearch };
        if (request.UsedSearchOnly)
            query = query.Where(item => item.UsedSearch &&
                item.Flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
                !item.Flight.PlannedServices.Any(service => service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService));
        var total = await query.LongCountAsync(cancellationToken);
        List<MobileFlightListItemDto> items = [];
        if (!paging.IsOutOfRange(total))
        {
            var station = await masterData.GetStationAsync(actor.StationId, cancellationToken);
            if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
                return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");
            // Capture one ordered page of identities and interaction times before enriching its existing cards.
            var page = await query.OrderByDescending(item => item.LastInteractionAtUtc).ThenBy(item => item.Flight.Id)
                .Skip(paging.Skip).Take(paging.PageSize)
                .Select(item => new { item.Flight.Id, item.LastInteractionAtUtc }).ToListAsync(cancellationToken);
            var ids = page.Select(item => item.Id).ToArray();
            var cards = await MobileFlightQuery.ProjectListItems(db.Flights.AsNoTracking().Where(item => ids.Contains(item.Id) &&
                    item.Station.StationId == actor.StationId && item.Schedule.Sta <= actor.WindowEndUtc && item.Schedule.Std >= actor.WindowStartUtc),
                actor, station.TimeZoneId).ToListAsync(cancellationToken);
            var versions = await logos.GetLogoVersionsAsync(cards.Select(item => item.CustomerId).Distinct().ToArray(), cancellationToken);
            var names = await FlightWorkAttribution.NamesAsync(userNames, cards.Select(item => item.CreatedByUserId), cancellationToken);
            var byId = cards.ToDictionary(item => item.Id);
            items = page.Where(item => byId.ContainsKey(item.Id)).Select(item => byId[item.Id] with
            {
                LastInteractionAtUtc = item.LastInteractionAtUtc,
                CreatedByDisplayName = byId[item.Id].CreatedByUserId is { } creator ? names.GetValueOrDefault(creator) : null,
                CustomerLogoVersion = versions.TryGetValue(byId[item.Id].CustomerId, out var version) ? version : CustomerLogoVersions.None
            }).ToList();
        }
        return new MobileFlightPageDto(items, paging.Page, paging.PageSize, total,
            actor.StationId, actor.AsOfUtc, actor.WindowStartUtc, actor.WindowEndUtc);
    }
}
