using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Contracts.Seeding;
using MasterData.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Services;

public sealed record GetServicesQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<ServiceListItemDto>>;

public sealed class GetServicesQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetServicesQuery, PagedResult<ServiceListItemDto>>
{
    public async Task<Result<PagedResult<ServiceListItemDto>>> Handle(GetServicesQuery request, CancellationToken cancellationToken)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.Services.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(s => s.IsActive == active);

        if (SearchFilter.Term(request.Search) is { } term)
            query = query.Where(s => s.Name.ToLower().Contains(term) || (s.Description != null && s.Description.ToLower().Contains(term)));

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<ServiceListItemDto>(total);

        var items = await ApplySort(query, request.Sort)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(s => new ServiceListItemDto(
                s.Id,
                s.Name,
                s.Description,
                s.IsActive,
                s.Id == WellKnownMasterDataIds.AircraftPerLandingService))
            .ToListAsync(cancellationToken);

        return paging.ToResult<ServiceListItemDto>(items, total);
    }

    private static IQueryable<Service> ApplySort(IQueryable<Service> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(s => s.Name).ThenBy(s => s.Id);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(s => s.Name).ThenByDescending(s => s.Id) : query.OrderBy(s => s.Name).ThenBy(s => s.Id),
            "isactive" => spec.Descending ? query.OrderByDescending(s => s.IsActive).ThenByDescending(s => s.Id) : query.OrderBy(s => s.IsActive).ThenBy(s => s.Id),
            _ => query.OrderBy(s => s.Name).ThenBy(s => s.Id)
        };
    }
}

public sealed record GetServiceByIdQuery(Guid Id) : IQuery<ServiceDto>;

public sealed class GetServiceByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetServiceByIdQuery, ServiceDto>
{
    public async Task<Result<ServiceDto>> Handle(GetServiceByIdQuery request, CancellationToken cancellationToken)
    {
        var service = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (service is null)
            return Error.NotFound("Service not found.", "MasterData.Service.NotFound");

        return new ServiceDto(
            service.Id, service.Name, service.Description, service.IsActive, ServiceSystemRecords.IsSystem(service.Id),
            service.CreatedAtUtc, service.UpdatedAtUtc, Convert.ToBase64String(service.RowVersion));
    }
}

internal static class ServiceSystemRecords
{
    public static bool IsSystem(Guid id) =>
        id == WellKnownMasterDataIds.AircraftPerLandingService;
}

public sealed record GetActiveServiceOptionsQuery : IQuery<IReadOnlyList<ServiceOptionDto>>;

public sealed class GetActiveServiceOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveServiceOptionsQuery, IReadOnlyList<ServiceOptionDto>>
{
    public async Task<Result<IReadOnlyList<ServiceOptionDto>>> Handle(GetActiveServiceOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceOptionDto> options = await db.Services.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Select(s => new ServiceOptionDto(s.Id, s.Name, s.Id == WellKnownMasterDataIds.AircraftPerLandingService))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
