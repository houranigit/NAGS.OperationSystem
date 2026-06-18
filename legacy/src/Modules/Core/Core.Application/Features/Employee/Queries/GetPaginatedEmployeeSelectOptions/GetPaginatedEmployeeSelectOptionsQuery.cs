using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Employee;

namespace Core.Application.Features.Employee.Queries.GetPaginatedEmployeeSelectOptions;

public sealed record GetPaginatedEmployeeSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<EmployeeSelectOption>>;
