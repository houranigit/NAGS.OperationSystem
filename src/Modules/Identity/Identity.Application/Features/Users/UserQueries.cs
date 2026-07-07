using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Users;

// --- Paged list -----------------------------------------------------------

public sealed record GetUsersQuery(int Page = 1, int PageSize = 20, string? Search = null, UserStatus? Status = null, Guid? RoleId = null, UserType? UserType = null, string? Sort = null)
    : IQuery<PagedResult<UserListItemDto>>;

public sealed class GetUsersQueryHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : IQueryHandler<GetUsersQuery, PagedResult<UserListItemDto>>
{
    public async Task<Result<PagedResult<UserListItemDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);
        var now = timeProvider.GetUtcNow();

        var query = db.Users.AsNoTracking();

        if (request.Status is { } status)
            query = query.Where(u => u.Status == status);

        if (request.RoleId is { } roleId)
            query = query.Where(u => u.RoleId == roleId);

        if (request.UserType is { } userType)
            query = query.Where(u => u.UserType == userType);

        if (SearchFilter.Term(request.Search) is { } term)
            query = query.Where(u => u.Email.Value.ToLower().Contains(term) || u.DisplayName.ToLower().Contains(term));

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<UserListItemDto>(total);

        var ordered = ApplySort(query, db, request.Sort);

        var items = await (
                from user in ordered
                join role in db.Roles.AsNoTracking() on user.RoleId equals role.Id into roleJoin
                from role in roleJoin.DefaultIfEmpty()
                select new { User = user, RoleName = role == null ? string.Empty : role.Name })
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(x => new UserListItemDto(
                x.User.Id,
                x.User.Email.Value,
                x.User.DisplayName,
                x.User.Status.ToString(),
                x.User.LockoutEndUtc != null && x.User.LockoutEndUtc > now,
                x.User.RoleId,
                x.RoleName,
                x.User.UserType.ToString(),
                x.User.ExternalReferenceId,
                x.User.CreatedAtUtc,
                x.User.LastLoginAtUtc))
            .ToListAsync(cancellationToken);

        return paging.ToResult<UserListItemDto>(items, total);
    }

    private static IQueryable<User> ApplySort(IQueryable<User> query, IIdentityDbContext db, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(u => u.DisplayName).ThenBy(u => u.Id);

        return spec.Field switch
        {
            "displayname" => spec.Descending ? query.OrderByDescending(u => u.DisplayName).ThenByDescending(u => u.Id) : query.OrderBy(u => u.DisplayName).ThenBy(u => u.Id),
            "email" => spec.Descending ? query.OrderByDescending(u => u.Email.Value).ThenByDescending(u => u.Id) : query.OrderBy(u => u.Email.Value).ThenBy(u => u.Id),
            "status" => spec.Descending ? query.OrderByDescending(u => u.Status).ThenByDescending(u => u.Id) : query.OrderBy(u => u.Status).ThenBy(u => u.Id),
            "createdat" => spec.Descending ? query.OrderByDescending(u => u.CreatedAtUtc).ThenByDescending(u => u.Id) : query.OrderBy(u => u.CreatedAtUtc).ThenBy(u => u.Id),
            "lastlogin" => spec.Descending ? query.OrderByDescending(u => u.LastLoginAtUtc).ThenByDescending(u => u.Id) : query.OrderBy(u => u.LastLoginAtUtc).ThenBy(u => u.Id),
            "rolename" => spec.Descending
                ? query.OrderByDescending(u => db.Roles.Where(r => r.Id == u.RoleId).Select(r => r.Name).FirstOrDefault()).ThenByDescending(u => u.Id)
                : query.OrderBy(u => db.Roles.Where(r => r.Id == u.RoleId).Select(r => r.Name).FirstOrDefault()).ThenBy(u => u.Id),
            _ => query.OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
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
            PortalSource.For(user.UserType), user.MfaEnabled, user.MfaRequired && !user.MfaEnabled,
            user.CreatedAtUtc, user.UpdatedAtUtc, user.LastLoginAtUtc);
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

        var permissions = EffectiveUserPermissions.For(user, role);

        return new AuthenticatedUserDto(
            user.Id, user.Email.Value, user.DisplayName,
            user.RoleId, role?.Name ?? string.Empty,
            user.UserType.ToString(), user.ExternalReferenceId, PortalSource.For(user.UserType),
            user.MfaEnabled, user.MfaRequired && !user.MfaEnabled,
            permissions);
    }
}

internal static class PortalSource
{
    /// <summary>How the account was provisioned: administrators are created directly; scoped accounts originate from MasterData.</summary>
    public static string For(BuildingBlocks.Contracts.Authorization.UserType userType) =>
        userType == BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator ? "Direct" : "MasterData";
}
