using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.ToolPricePlan;

namespace Store.Application.Features.ToolPricePlan.Queries.GetPaginatedToolPricePlans;

public sealed record GetPaginatedToolPricePlansQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ToolPricePlanDto>>;
