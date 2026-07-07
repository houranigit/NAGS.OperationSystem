using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Contracts.Seeding;
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
        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.OperationTypes.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(o => o.IsActive == active);

        if (SearchFilter.Term(request.Search) is { } term)
            query = query.Where(o => o.Name.ToLower().Contains(term) || (o.Description != null && o.Description.ToLower().Contains(term)));

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<OperationTypeListItemDto>(total);

        var items = await ApplySort(query, request.Sort)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(o => new OperationTypeListItemDto(
                o.Id,
                o.Name,
                o.Description,
                o.IsActive,
                o.Id == WellKnownMasterDataIds.AdHocOperationType))
            .ToListAsync(cancellationToken);

        return paging.ToResult<OperationTypeListItemDto>(items, total);
    }

    private static IQueryable<OperationType> ApplySort(IQueryable<OperationType> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(o => o.Name).ThenBy(o => o.Id);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(o => o.Name).ThenByDescending(o => o.Id) : query.OrderBy(o => o.Name).ThenBy(o => o.Id),
            "isactive" => spec.Descending ? query.OrderByDescending(o => o.IsActive).ThenByDescending(o => o.Id) : query.OrderBy(o => o.IsActive).ThenBy(o => o.Id),
            _ => query.OrderBy(o => o.Name).ThenBy(o => o.Id)
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
            operationType.Id, operationType.Name, operationType.Description, operationType.IsActive, OperationTypeSystemRecords.IsSystem(operationType.Id),
            operationType.CreatedAtUtc, operationType.UpdatedAtUtc, Convert.ToBase64String(operationType.RowVersion));
    }
}

internal static class OperationTypeSystemRecords
{
    public static bool IsSystem(Guid id) => id == WellKnownMasterDataIds.AdHocOperationType;
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
            .ThenBy(o => o.Id)
            .Select(o => new OperationTypeOptionDto(o.Id, o.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
