using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.ServicePricePlan;

namespace Core.Application.Features.ServicePricePlan.Queries.GetPaginatedServicePricePlans;

public sealed record GetPaginatedServicePricePlansQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ServicePricePlanDto>>;
