using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.MaterialPricePlan;

namespace Store.Application.Features.MaterialPricePlan.Queries.GetPaginatedMaterialPricePlans;

public sealed record GetPaginatedMaterialPricePlansQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<MaterialPricePlanDto>>;
