using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Station;
using Identity.Domain.Authorization;

namespace Core.Application.Features.Station.Queries.GetPaginatedStationSelectOptions;

public sealed record GetPaginatedStationSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<StationSelectOption>>, IRequirePermission
{
    public string RequiredPermission => Permissions.Scheduler.ReadLookups;
}
