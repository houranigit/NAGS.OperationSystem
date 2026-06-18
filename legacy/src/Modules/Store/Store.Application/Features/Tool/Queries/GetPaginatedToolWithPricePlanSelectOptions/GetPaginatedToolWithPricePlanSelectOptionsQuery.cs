using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.Tool;

namespace Store.Application.Features.Tool.Queries.GetPaginatedToolWithPricePlanSelectOptions;

/// <summary>
/// Paginated select-option list of tools, each row enriched with its system-default
/// <see cref="ToolPricePlan"/> (zero or one entry, since Store plans are not OT-scoped).
/// Powers the contract wizard's auto-fill flow.
/// </summary>
public sealed record GetPaginatedToolWithPricePlanSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ToolWithPricePlanSelectOption>>;
