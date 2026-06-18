using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomerSelectOptions;
using Core.Contracts.Features.Pricing;
using Core.Contracts.Features.Service;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Service.Queries.GetPaginatedServiceWithPricePlanSelectOptions;

/// <summary>
/// Pipeline mirrors <see cref="GetPaginatedCustomerSelectOptionsQueryHandler"/>: stays on
/// <see cref="IQueryable{T}"/> from filter to <c>ToListAsync</c>, projects after paging.
/// </summary>
/// <remarks>
/// The plan lookup is a correlated subquery so the cost stays linear in the page size, not in
/// the global plan count. Includes only active plans; the wizard uses the strictest match
/// (OperationType + AircraftType then OperationType + null) when picking a default.
/// </remarks>
public sealed class GetPaginatedServiceWithPricePlanSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedServiceWithPricePlanSelectOptionsQuery, PaginatedResult<ServiceWithPricePlanSelectOption>>
{
    public async Task<Result<PaginatedResult<ServiceWithPricePlanSelectOption>>> Handle(
        GetPaginatedServiceWithPricePlanSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Services.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new ServiceWithPricePlanSelectOption(
                s.Id.Value,
                s.Name,
                db.ServicePricePlans
                    .Where(p => p.IsActive && p.ServiceId == s.Id)
                    .Select(p => new PricePlanScopeOption(
                        p.Id.Value,
                        p.OperationTypeId.Value,
                        p.AircraftTypeId == null ? (Guid?)null : p.AircraftTypeId.Value,
                        p.CurrencyId.Value,
                        p.Basis,
                        p.Brackets.Select(b => new PriceBracketDto(
                            b.MinMinutes,
                            b.MaxMinutes,
                            b.BlockSize,
                            b.Value,
                            b.BillingMode)).ToList()))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ServiceWithPricePlanSelectOption>(items, total, request.Page, request.PageSize);
    }
}
