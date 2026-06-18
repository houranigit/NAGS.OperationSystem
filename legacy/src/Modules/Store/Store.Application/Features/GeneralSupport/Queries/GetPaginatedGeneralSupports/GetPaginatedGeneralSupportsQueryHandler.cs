using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Unit;

namespace Store.Application.Features.GeneralSupport.Queries.GetPaginatedGeneralSupports;

public sealed class GetPaginatedGeneralSupportsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedGeneralSupportsQuery, PaginatedResult<GeneralSupportDto>>
{
    public async Task<Result<PaginatedResult<GeneralSupportDto>>> Handle(
        GetPaginatedGeneralSupportsQuery request,
        CancellationToken cancellationToken)
    {
        var baseQuery = db.GeneralSupports.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            baseQuery = baseQuery.Where(request.FilterQuery);

        var total = baseQuery.Count();

        var ordered = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? baseQuery.OrderBy(request.OrderByQuery)
            : baseQuery.OrderBy(x => x.Name);

        var skip = (request.Page - 1) * request.PageSize;

        var items = await (
                from g in ordered.Skip(skip).Take(request.PageSize)
                join u in db.Units on g.UnitId equals u.Id
                select new GeneralSupportDto(
                    g.Id.Value,
                    g.Name,
                    new UnitSnapshot(u.Id.Value, u.Code, u.Name),
                    g.IsDuration,
                    g.Note,
                    g.IsActive,
                    g.CreatedAt,
                    g.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<GeneralSupportDto>(items, total, request.Page, request.PageSize);
    }
}
