using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Customer;

namespace Core.Application.Features.Customer.Queries.GetPaginatedCustomerSelectOptions;

public sealed record GetPaginatedCustomerSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<CustomerSelectOption>>;
