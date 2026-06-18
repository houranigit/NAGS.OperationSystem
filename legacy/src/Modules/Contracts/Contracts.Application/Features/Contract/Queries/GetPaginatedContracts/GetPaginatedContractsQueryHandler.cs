using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Contracts.Application.Abstractions;
using Contracts.Application.Features.Contract.Shared;
using Contracts.Contracts.Contract;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace Contracts.Application.Features.Contract.Queries.GetPaginatedContracts;

/// <summary>
/// Filter → count → order → page on the root, then <c>Include</c> the children before
/// projecting to <see cref="ContractDto"/>. <c>Include</c> after pagination is fine here
/// because it joins only on the small page slice.
/// </summary>
public sealed class GetPaginatedContractsQueryHandler(IContractsDbContext db)
    : IQueryHandler<GetPaginatedContractsQuery, PaginatedResult<ContractDto>>
{
    public async Task<Result<PaginatedResult<ContractDto>>> Handle(
        GetPaginatedContractsQuery request,
        CancellationToken cancellationToken)
    {
        var rootQuery = db.Contracts.AsQueryable();

        if (request.CustomerId is { } customerId && customerId != Guid.Empty)
            rootQuery = rootQuery.Where(c => c.CustomerId == customerId);

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            rootQuery = rootQuery.Where(request.FilterQuery);

        var total = rootQuery.Count();

        rootQuery = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? rootQuery.OrderBy(request.OrderByQuery)
            : rootQuery.OrderByDescending(c => c.CreatedAt);

        var pagedIds = await rootQuery
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (pagedIds.Count == 0)
            return new PaginatedResult<ContractDto>([], total, request.Page, request.PageSize);

        var loaded = await db.Contracts
            .Include(c => c.Stations)
            .Include(c => c.OperationTypes)
            .Include(c => c.Services).ThenInclude(s => s.Brackets)
            .Include(c => c.Manpowers).ThenInclude(m => m.Brackets)
            .Include(c => c.Tools).ThenInclude(t => t.Brackets)
            .Include(c => c.Materials).ThenInclude(m => m.Brackets)
            .Include(c => c.GeneralSupports).ThenInclude(g => g.Brackets)
            .Include(c => c.CancellationBrackets)
            .Include(c => c.DelayBrackets)
            .Where(c => pagedIds.Contains(c.Id))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        // Preserve the page order of the root query.
        var ordered = pagedIds
            .Select(id => loaded.FirstOrDefault(c => c.Id == id))
            .Where(c => c is not null)!
            .Select(c => ContractDtoProjection.ToDto(c!))
            .ToList();

        return new PaginatedResult<ContractDto>(ordered, total, request.Page, request.PageSize);
    }
}
