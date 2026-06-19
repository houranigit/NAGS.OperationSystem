using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

// --- Permission catalog ---------------------------------------------------

public sealed record GetPermissionCatalogQuery : IQuery<IReadOnlyList<PermissionGroupDto>>;

public sealed class GetPermissionCatalogQueryHandler
    : IQueryHandler<GetPermissionCatalogQuery, IReadOnlyList<PermissionGroupDto>>
{
    public Task<Result<IReadOnlyList<PermissionGroupDto>>> Handle(GetPermissionCatalogQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionGroupDto> groups = IdentityPermissions.All
            .GroupBy(p => p.Split('.') is { Length: >= 2 } parts ? parts[1] : p)
            .Select(g => new PermissionGroupDto(g.Key, g.ToList()))
            .ToList();

        return Task.FromResult(Result.Success(groups));
    }
}

// --- Paged list -----------------------------------------------------------

public sealed record GetRolesQuery(int Page = 1, int PageSize = 20, string? Search = null)
    : IQuery<PagedResult<RoleListItemDto>>;

public sealed class GetRolesQueryHandler(IIdentityDbContext db)
    : IQueryHandler<GetRolesQuery, PagedResult<RoleListItemDto>>
{
    public async Task<Result<PagedResult<RoleListItemDto>>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.Roles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
            query = query.Where(r => r.NormalizedName.Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var roles = await query
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var roleIds = roles.Select(r => r.Id).ToList();
        var userCounts = await db.Users.AsNoTracking()
            .Where(u => roleIds.Contains(u.RoleId))
            .GroupBy(u => u.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);

        var items = roles.Select(r => new RoleListItemDto(
            r.Id,
            r.Name,
            r.Description,
            r.IsSystem,
            r.Permissions.Count,
            userCounts.GetValueOrDefault(r.Id))).ToList();

        return new PagedResult<RoleListItemDto>(items, page, pageSize, total);
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetRoleByIdQuery(Guid Id) : IQuery<RoleDto>;

public sealed class GetRoleByIdQueryHandler(IIdentityDbContext db)
    : IQueryHandler<GetRoleByIdQuery, RoleDto>
{
    public async Task<Result<RoleDto>> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (role is null)
            return Error.NotFound("Role not found.", "Identity.Role.NotFound");

        return new RoleDto(
            role.Id, role.Name, role.Description, role.IsSystem,
            role.Permissions.ToList(), role.CreatedAtUtc, role.UpdatedAtUtc);
    }
}
