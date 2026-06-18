using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.ManpowerPricePlan;

namespace Core.Application.Features.ManpowerPricePlan.Queries.GetPaginatedManpowerPricePlans;

public sealed record GetPaginatedManpowerPricePlansQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ManpowerPricePlanDto>>;
