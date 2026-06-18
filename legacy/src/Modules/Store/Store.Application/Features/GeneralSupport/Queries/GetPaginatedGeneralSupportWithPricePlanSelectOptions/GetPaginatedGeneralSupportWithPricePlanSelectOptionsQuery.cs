using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.GeneralSupport;

namespace Store.Application.Features.GeneralSupport.Queries.GetPaginatedGeneralSupportWithPricePlanSelectOptions;

/// <summary>
/// Paginated select-option list of general-support items, each row enriched with its
/// system-default <see cref="GeneralSupportPricePlan"/> (zero or one entry). Powers the
/// contract wizard's auto-fill flow.
/// </summary>
public sealed record GetPaginatedGeneralSupportWithPricePlanSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<GeneralSupportWithPricePlanSelectOption>>;
