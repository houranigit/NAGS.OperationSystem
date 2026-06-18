using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Contracts.Contracts.Contract;

namespace Contracts.Application.Features.Contract.Queries.GetPaginatedContractsLight;

/// <summary>
/// Cheap grid query — projects a single SQL select per row and never loads child
/// collections. Use for list views, dashboards, picker drop-downs.
/// </summary>
/// <param name="CustomerId">When set, restricts the result to contracts that belong to this customer.</param>
public sealed record GetPaginatedContractsLightQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null,
    Guid? CustomerId = null
) : IQuery<PaginatedResult<ContractSummary>>;
