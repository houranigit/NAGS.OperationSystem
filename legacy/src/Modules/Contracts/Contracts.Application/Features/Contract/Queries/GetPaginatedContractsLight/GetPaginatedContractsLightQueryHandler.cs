using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Contracts.Application.Abstractions;
using Contracts.Contracts.Contract;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;
using CoreCustomer = Core.Contracts.Features.Customer.CustomerSnapshot;
using CoreCurrency = Core.Contracts.Features.Currency.CurrencySnapshot;

namespace Contracts.Application.Features.Contract.Queries.GetPaginatedContractsLight;

/// <summary>
/// Mirrors <c>GetPaginatedCustomersQueryHandler</c>: filter → count → order → page →
/// project to <see cref="ContractSummary"/> in a single <c>ToListAsync</c>.
/// </summary>
public sealed class GetPaginatedContractsLightQueryHandler(IContractsDbContext db)
    : IQueryHandler<GetPaginatedContractsLightQuery, PaginatedResult<ContractSummary>>
{
    public async Task<Result<PaginatedResult<ContractSummary>>> Handle(
        GetPaginatedContractsLightQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Contracts.AsQueryable();

        if (request.CustomerId is { } customerId && customerId != Guid.Empty)
            query = query.Where(c => c.CustomerId == customerId);

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderByDescending(c => c.CreatedAt);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new ContractSummary(
                c.Id.Value,
                c.ContractNo.Value,
                new CoreCustomer(c.Customer.CustomerId, c.Customer.IataCode, c.Customer.Name),
                new CoreCurrency(c.Currency.CurrencyId, c.Currency.Code),
                new ContractPeriod(
                    c.Period.StartDate,
                    c.Period.ExpiryDate,
                    c.Period.ExpiryAlertDays,
                    c.Period.ExpiryAlertInterval),
                c.Status,
                c.PaymentTerms,
                c.ApplyVat,
                c.CreatedByUserId,
                c.CreatedAt,
                c.UpdatedByUserId,
                c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ContractSummary>(items, total, request.Page, request.PageSize);
    }
}
