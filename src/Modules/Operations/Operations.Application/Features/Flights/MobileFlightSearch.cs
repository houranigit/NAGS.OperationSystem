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

public sealed record GetMobileFlightSearchQuery(string FlightNumber, int Page = 1, int PageSize = 20) : IQuery<MobileFlightPageDto>;

/// <summary>Exact discovery of regular flights; the normal feed remains assigned-only for this category.</summary>
public sealed class GetMobileFlightSearchQueryHandler(IOperationsDbContext db, IMobileFlightScope scope,
    ICustomerLogoReader logos, IMasterDataReader masterData, IUserDisplayNameReader? userNames = null)
    : IQueryHandler<GetMobileFlightSearchQuery, MobileFlightPageDto>
{
    public async Task<Result<MobileFlightPageDto>> Handle(GetMobileFlightSearchQuery request, CancellationToken ct)
    {
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var number = request.FlightNumber?.Trim().ToUpperInvariant();
        var paging = PageRequest.From(request.Page, request.PageSize);
        if (string.IsNullOrEmpty(number)) return new MobileFlightPageDto([], paging.Page, paging.PageSize, 0,
            actor.StationId, actor.AsOfUtc, actor.WindowStartUtc, actor.WindowEndUtc);
        var query = MobileFlightQuery.ApplyReadScope(db.Flights.AsNoTracking(), actor).Where(flight =>
            flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
            !flight.PlannedServices.Any(item => item.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) &&
            flight.FlightNumber.Value.Trim().ToUpper() == number);
        var total = await query.LongCountAsync(ct);
        List<MobileFlightListItemDto> items = [];
        if (!paging.IsOutOfRange(total))
        {
            var station = await masterData.GetStationAsync(actor.StationId, ct);
            if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
                return Error.Failure("Flight data could not be loaded.", "Operations.Flight.StationDataUnavailable");
            items = await MobileFlightQuery.ProjectListItems(query.OrderByDescending(item => item.Schedule.Sta).ThenBy(item => item.Id)
                .Skip(paging.Skip).Take(paging.PageSize), actor, station.TimeZoneId).ToListAsync(ct);
            var versions = await logos.GetLogoVersionsAsync(items.Select(item => item.CustomerId).Distinct().ToArray(), ct);
            var names = await FlightWorkAttribution.NamesAsync(userNames, items.Select(item => item.CreatedByUserId), ct);
            items = items.Select(item => item with
            {
                CustomerLogoVersion = versions.GetValueOrDefault(item.CustomerId) ?? CustomerLogoVersions.None,
                CreatedByDisplayName = item.CreatedByUserId is { } creator ? names.GetValueOrDefault(creator) : null
            }).ToList();
        }
        return new MobileFlightPageDto(items, paging.Page, paging.PageSize, total, actor.StationId, actor.AsOfUtc, actor.WindowStartUtc, actor.WindowEndUtc);
    }
}
