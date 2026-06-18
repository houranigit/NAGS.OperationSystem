using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.ManpowerType;

namespace Core.Application.Features.ManpowerType.Queries.GetPaginatedManpowerTypeWithPricePlanSelectOptions;

/// <summary>
/// Paginated select-option list of manpower types, each row enriched with its system-default
/// <see cref="ManpowerPricePlan"/>(s). Powers the contract wizard's "select a manpower type →
/// auto-fill brackets" flow (one plan per OperationType).
/// </summary>
public sealed record GetPaginatedManpowerTypeWithPricePlanSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ManpowerTypeWithPricePlanSelectOption>>;
