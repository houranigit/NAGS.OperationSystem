using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Customer;
using Identity.Domain.Authorization;

namespace Core.Application.Features.Customer.Queries.GetPaginatedCustomers;

public sealed record GetPaginatedCustomersQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<CustomerDto>>, IRequirePermission
{
    public string RequiredPermission => Permissions.Scheduler.ReadLookups;
}
