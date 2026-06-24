using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Users;

// --- Paged list -----------------------------------------------------------

public sealed record GetUsersQuery(int Page = 1, int PageSize = 20, string? Search = null, UserStatus? Status = null, Guid? RoleId = null, string? Sort = null)
    : IQuery<PagedResult<UserListItemDto>>;

public sealed class GetUsersQueryHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : IQueryHandler<GetUsersQuery, PagedResult<UserListItemDto>>
{
    public async Task<Result<PagedResult<UserListItemDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var now = timeProvider.GetUtcNow();

        var query = db.Users.AsNoTracking();

        if (request.Status is { } status)
            query = query.Where(u => u.Status == status);

        if (request.RoleId is { } roleId)
            query = query.Where(u => u.RoleId == roleId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var lower = term.ToLowerInvariant();
            query = query.Where(u => u.Email.Value.Contains(lower) || u.DisplayName.Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var users = await ApplySort(query, db, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var roleIds = users.Select(u => u.RoleId).Distinct().ToList();
        var roleNames = await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var items = users.Select(u => new UserListItemDto(
            u.Id,
            u.Email.Value,
            u.DisplayName,
            u.Status.ToString(),
            u.IsLockedOut(now),
            u.RoleId,
            roleNames.GetValueOrDefault(u.RoleId, string.Empty),
            u.UserType.ToString(),
            u.ExternalReferenceId,
            u.CreatedAtUtc,
            u.LastLoginAtUtc)).ToList();

        return new PagedResult<UserListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<User> ApplySort(IQueryable<User> query, IIdentityDbContext db, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(u => u.DisplayName);

        return spec.Field switch
        {
            "displayname" => spec.Descending ? query.OrderByDescending(u => u.DisplayName) : query.OrderBy(u => u.DisplayName),
            "email" => spec.Descending ? query.OrderByDescending(u => u.Email.Value) : query.OrderBy(u => u.Email.Value),
            "status" => spec.Descending ? query.OrderByDescending(u => u.Status) : query.OrderBy(u => u.Status),
            "createdat" => spec.Descending ? query.OrderByDescending(u => u.CreatedAtUtc) : query.OrderBy(u => u.CreatedAtUtc),
            "lastlogin" => spec.Descending ? query.OrderByDescending(u => u.LastLoginAtUtc) : query.OrderBy(u => u.LastLoginAtUtc),
            "rolename" => spec.Descending
                ? query.OrderByDescending(u => db.Roles.Where(r => r.Id == u.RoleId).Select(r => r.Name).FirstOrDefault())
                : query.OrderBy(u => db.Roles.Where(r => r.Id == u.RoleId).Select(r => r.Name).FirstOrDefault()),
            _ => query.OrderBy(u => u.DisplayName)
        };
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetUserByIdQuery(Guid Id) : IQuery<UserDto>;

public sealed class GetUserByIdQueryHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : IQueryHandler<GetUserByIdQuery, UserDto>
{
    public async Task<Result<UserDto>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var roleName = await db.Roles.AsNoTracking()
            .Where(r => r.Id == user.RoleId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new UserDto(
            user.Id, user.Email.Value, user.DisplayName, user.Status.ToString(),
            user.IsLockedOut(timeProvider.GetUtcNow()), user.LockoutEndUtc,
            user.RoleId, roleName, user.UserType.ToString(), user.ExternalReferenceId,
            PortalSource.For(user.UserType), user.CreatedAtUtc, user.UpdatedAtUtc, user.LastLoginAtUtc);
    }
}

// --- Current user (me) ----------------------------------------------------

public sealed record GetCurrentUserQuery : IQuery<AuthenticatedUserDto>;

public sealed class GetCurrentUserQueryHandler(IIdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetCurrentUserQuery, AuthenticatedUserDto>
{
    public async Task<Result<AuthenticatedUserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.Unauthorized();

        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);

        return new AuthenticatedUserDto(
            user.Id, user.Email.Value, user.DisplayName,
            user.RoleId, role?.Name ?? string.Empty,
            user.UserType.ToString(), user.ExternalReferenceId, PortalSource.For(user.UserType),
            user.MfaEnabled, user.MfaRequired && !user.MfaEnabled,
            role?.Permissions.ToList() ?? []);
    }
}

internal static class PortalSource
{
    /// <summary>How the account was provisioned: administrators are created directly; scoped accounts originate from MasterData.</summary>
    public static string For(BuildingBlocks.Contracts.Authorization.UserType userType) =>
        userType == BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator ? "Direct" : "MasterData";
}
