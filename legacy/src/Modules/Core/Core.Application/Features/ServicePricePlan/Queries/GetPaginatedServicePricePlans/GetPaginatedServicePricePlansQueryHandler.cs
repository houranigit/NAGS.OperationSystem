using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Currency;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Service;
using Core.Contracts.Features.ServicePricePlan;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.ServicePricePlan.Queries.GetPaginatedServicePricePlans;

/// <summary>
/// Paginated grid query for service price plans — same pipeline as <see cref="Features.Customer.Queries.GetPaginatedCustomers.GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// Stays on <see cref="IQueryable{T}"/> through filter, count, sort, and page; projects to <see cref="ServicePricePlanDto"/> in one
/// terminal <c>Select</c> + <c>ToListAsync</c>. Joins reference data (service, operation type, currency, optional aircraft) in the database.
/// </remarks>
public sealed class GetPaginatedServicePricePlansQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedServicePricePlansQuery, PaginatedResult<ServicePricePlanDto>>
{
    public async Task<Result<PaginatedResult<ServicePricePlanDto>>> Handle(
        GetPaginatedServicePricePlansQuery request,
        CancellationToken cancellationToken)
    {
        var query =
            from p in db.ServicePricePlans
            join s in db.Services on p.ServiceId equals s.Id
            join ot in db.OperationTypes on p.OperationTypeId equals ot.Id
            join curr in db.Currencies on p.CurrencyId equals curr.Id
            join at in db.AircraftTypes on p.AircraftTypeId equals at.Id into atGroup
            from at in atGroup.DefaultIfEmpty()
            select new
            {
                p,
                s,
                ot,
                curr,
                at,
                Basis = p.Basis,
            };

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = await query.CountAsync(cancellationToken);

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.s.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new ServicePricePlanDto(
                x.p.Id.Value,
                new ServiceSnapshot(x.s.Id.Value, x.s.Name),
                new OperationTypeSnapshot(x.ot.Id.Value, x.ot.Name),
                x.at == null ? null : new AircraftTypeSnapshot(x.at.Id.Value, x.at.Model),
                new CurrencySnapshot(x.curr.Id.Value, x.curr.Code.Value),
                x.p.Basis,
                x.p.Brackets.Select(b => new ServicePricePlanBracketDto(
                    b.MinMinutes,
                    b.MaxMinutes,
                    b.BlockSize,
                    b.Value,
                    b.BillingMode)).ToList(),
                x.p.CreatedAt,
                x.p.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ServicePricePlanDto>(items, total, request.Page, request.PageSize);
    }
}
