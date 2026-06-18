using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomers;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomerSelectOptions;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Seeding;
using Core.Domain.Aggregates.OperationType;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.OperationType.Queries.GetPaginatedOperationTypeSelectOptions;

/// <summary>
/// Paginated select rows for Host.Web dropdowns. Pipeline mirrors
/// <see cref="GetPaginatedCustomerSelectOptionsQueryHandler"/> and <see cref="GetPaginatedCustomersQueryHandler"/>.
/// </summary>
public sealed class GetPaginatedOperationTypeSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedOperationTypeSelectOptionsQuery, PaginatedResult<OperationTypeSelectOption>>
{
    public async Task<Result<PaginatedResult<OperationTypeSelectOption>>> Handle(
        GetPaginatedOperationTypeSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Baseline query — stay on IQueryable until the final ToListAsync.
        var query = db.OperationTypes.Where(x => x.IsActive).AsQueryable();

        // Optional "Ad Hoc"-exclusion (used by the contract dialog — domain forbids it on
        // contracts). Compares strongly-typed id so EF translates cleanly without VO unwrap.
        if (!request.IncludeAdHoc)
        {
            var adHocId = OperationTypeId.From(CoreSeedIds.AdHocOperationType);
            query = query.Where(x => x.Id != adHocId);
        }

        // 2. Dynamic filters (entity property names).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total before paging.
        var total = query.Count();

        // 4. Sort.
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        // 5–7. Page, project, single ToListAsync.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new OperationTypeSelectOption(x.Id.Value, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<OperationTypeSelectOption>(items, total, request.Page, request.PageSize);
    }
}
