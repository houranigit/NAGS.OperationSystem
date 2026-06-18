using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Employee;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Employee.Queries.GetPaginatedEmployeeSelectOptions;

/// <summary>
/// Dropdown / lookup rows for employees — same <see cref="IQueryable{T}"/> pipeline as
/// <see cref="Customer.Queries.GetPaginatedCustomers.GetPaginatedCustomersQueryHandler"/>
/// (filter → count → order → page → join → single <c>ToListAsync</c>).
/// </summary>
public sealed class GetPaginatedEmployeeSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedEmployeeSelectOptionsQuery, PaginatedResult<EmployeeSelectOption>>
{
    public async Task<Result<PaginatedResult<EmployeeSelectOption>>> Handle(
        GetPaginatedEmployeeSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Employees.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(e => e.FullName);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Join(db.ManpowerTypes, e => e.ManpowerTypeId, mt => mt.Id, (e, mt) =>
                new EmployeeSelectOption(e.Id.Value, e.FullName, e.Email, mt.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<EmployeeSelectOption>(items, total, request.Page, request.PageSize);
    }
}
