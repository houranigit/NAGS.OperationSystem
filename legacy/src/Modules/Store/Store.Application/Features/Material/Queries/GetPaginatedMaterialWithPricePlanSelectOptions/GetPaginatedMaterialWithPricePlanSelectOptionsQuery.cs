using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.Material;

namespace Store.Application.Features.Material.Queries.GetPaginatedMaterialWithPricePlanSelectOptions;

/// <summary>
/// Paginated select-option list of materials, each row enriched with its system-default
/// <see cref="MaterialPricePlan"/> (zero or one entry). Powers the contract wizard's auto-fill flow.
/// </summary>
public sealed record GetPaginatedMaterialWithPricePlanSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<MaterialWithPricePlanSelectOption>>;
