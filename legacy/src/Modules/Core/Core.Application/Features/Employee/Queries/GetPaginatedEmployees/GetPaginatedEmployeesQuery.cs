using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Employee;
using Identity.Domain.Authorization;

namespace Core.Application.Features.Employee.Queries.GetPaginatedEmployees;

public sealed record GetPaginatedEmployeesQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<EmployeeDto>>, IRequirePermission
{
    public string RequiredPermission => Permissions.Scheduler.ReadLookups;
}
