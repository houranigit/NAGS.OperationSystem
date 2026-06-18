using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Service;

namespace Core.Application.Features.Service.Queries.GetPaginatedServiceWithPricePlanSelectOptions;

/// <summary>
/// Paginated select-option list of services, each row enriched with its system-default
/// <see cref="ServicePricePlan"/>(s). Powers the contract wizard's "select a service →
/// auto-fill brackets" flow.
/// </summary>
public sealed record GetPaginatedServiceWithPricePlanSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ServiceWithPricePlanSelectOption>>;
