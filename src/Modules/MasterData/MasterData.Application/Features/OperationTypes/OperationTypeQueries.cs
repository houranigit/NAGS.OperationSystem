using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.OperationTypes;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.OperationTypes;

public sealed record GetOperationTypesQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<OperationTypeListItemDto>>;

public sealed class GetOperationTypesQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetOperationTypesQuery, PagedResult<OperationTypeListItemDto>>
{
    public async Task<Result<PagedResult<OperationTypeListItemDto>>> Handle(GetOperationTypesQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = db.OperationTypes.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(o => o.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(o => o.Name.Contains(term) || (o.Description != null && o.Description.Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OperationTypeListItemDto(o.Id, o.Name, o.Description, o.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<OperationTypeListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<OperationType> ApplySort(IQueryable<OperationType> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(o => o.Name);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(o => o.Name) : query.OrderBy(o => o.Name),
            "isactive" => spec.Descending ? query.OrderByDescending(o => o.IsActive) : query.OrderBy(o => o.IsActive),
            _ => query.OrderBy(o => o.Name)
        };
    }
}

public sealed record GetOperationTypeByIdQuery(Guid Id) : IQuery<OperationTypeDto>;

public sealed class GetOperationTypeByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetOperationTypeByIdQuery, OperationTypeDto>
{
    public async Task<Result<OperationTypeDto>> Handle(GetOperationTypeByIdQuery request, CancellationToken cancellationToken)
    {
        var operationType = await db.OperationTypes.AsNoTracking().FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (operationType is null)
            return Error.NotFound("Operation type not found.", "MasterData.OperationType.NotFound");

        return new OperationTypeDto(
            operationType.Id, operationType.Name, operationType.Description, operationType.IsActive,
            operationType.CreatedAtUtc, operationType.UpdatedAtUtc, Convert.ToBase64String(operationType.RowVersion));
    }
}

public sealed record GetActiveOperationTypeOptionsQuery : IQuery<IReadOnlyList<OperationTypeOptionDto>>;

public sealed class GetActiveOperationTypeOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveOperationTypeOptionsQuery, IReadOnlyList<OperationTypeOptionDto>>
{
    public async Task<Result<IReadOnlyList<OperationTypeOptionDto>>> Handle(GetActiveOperationTypeOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<OperationTypeOptionDto> options = await db.OperationTypes.AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.Name)
            .Select(o => new OperationTypeOptionDto(o.Id, o.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
