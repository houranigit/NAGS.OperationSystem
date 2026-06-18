using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.GeneralSupportPricePlan;

namespace Store.Application.Features.GeneralSupportPricePlan.Queries.GetPaginatedGeneralSupportPricePlans;

public sealed record GetPaginatedGeneralSupportPricePlansQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<GeneralSupportPricePlanDto>>;
