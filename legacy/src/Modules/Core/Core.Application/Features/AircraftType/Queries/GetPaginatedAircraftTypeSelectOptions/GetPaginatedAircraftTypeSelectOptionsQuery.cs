using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.AircraftType;
using Identity.Domain.Authorization;

namespace Core.Application.Features.AircraftType.Queries.GetPaginatedAircraftTypeSelectOptions;

public sealed record GetPaginatedAircraftTypeSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<AircraftTypeSelectOption>>, IRequirePermission
{
    public string RequiredPermission => Permissions.Scheduler.ReadLookups;
}
