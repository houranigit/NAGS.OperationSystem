using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Contracts.Contracts.Contract;

namespace Contracts.Application.Features.Contract.Queries.GetPaginatedContracts;

/// <summary>
/// Full-aggregate paginated query — loads child collections on the page slice. Costlier than
/// <c>GetPaginatedContractsLightQuery</c>; reach for it only when the consumer truly needs
/// the child rows (e.g. an export endpoint, the customer-profile contracts list).
/// </summary>
/// <param name="CustomerId">When set, restricts the result to contracts that belong to this customer.</param>
public sealed record GetPaginatedContractsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    Guid? CustomerId = null
) : IQuery<PaginatedResult<ContractDto>>;
