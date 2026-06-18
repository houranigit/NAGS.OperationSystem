using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Contracts.Features.User;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Queries.GetPaginatedUsers;

/// <summary>
/// Paginated grid query for users — keeps the projection on <see cref="IQueryable{T}"/> until a
/// single terminal materialization, mirroring <c>GetPaginatedEmployeesQueryHandler</c>.
/// Role names are resolved with a sub-Select against the snapshot of <see cref="IIdentityDbContext.Roles"/>
/// so we don't ship the entire role table to the client.
/// </summary>
public sealed class GetPaginatedUsersQueryHandler(IIdentityDbContext db)
    : IQueryHandler<GetPaginatedUsersQuery, PaginatedResult<UserDto>>
{
    public async Task<Result<PaginatedResult<UserDto>>> Handle(
        GetPaginatedUsersQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(u => u.Username.Value);

        var pageRows = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new UserPageRow(
                u.Id.Value,
                u.Username.Value,
                u.Email.Value,
                u.UserType.Name,
                u.Status.Name,
                u.LockedUntil,
                u.CreatedAt,
                u.LastPasswordChangedAt,
                u.PasswordExpiresAt,
                u.FailedLoginAttempts,
                u.InvitationToken != null,
                u.InvitationTokenExpiresAt,
                u.InvitationToken,
                u.ExternalReferenceId,
                u.Roles.Select(r => new UserRolePageRow(
                    r.RoleId.Value,
                    db.Roles.Where(role => role.Id == r.RoleId)
                        .Select(role => role.Name)
                        .FirstOrDefault() ?? ""))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var items = pageRows.Select(ToDto).ToList();
        return new PaginatedResult<UserDto>(items, total, request.Page, request.PageSize);
    }

    private static UserDto ToDto(UserPageRow r)
    {
        var isLocked = r.LockedUntil.HasValue && r.LockedUntil.Value > DateTime.UtcNow;
        var isActive = string.Equals(r.Status, "Active", StringComparison.Ordinal);

        var roles = r.Roles
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new UserRoleSnapshot(x.RoleId, x.Name))
            .ToList();

        var invitationToken = InvitationTokenVisible(r.InvitationToken, r.InvitationExpiresAt);

        return new UserDto(
            r.Id,
            r.Username,
            r.Email,
            r.UserType,
            r.Status,
            isActive,
            isLocked,
            r.LockedUntil,
            r.CreatedAt,
            r.LastPasswordChangedAt,
            r.PasswordExpiresAt,
            r.FailedLoginAttempts,
            r.HasPendingInvitation,
            r.InvitationExpiresAt,
            invitationToken,
            r.ExternalReferenceId,
            roles);
    }

    /// <summary>Do not expose expired tokens — operator should resend invite instead.</summary>
    private static string? InvitationTokenVisible(string? token, DateTime? expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        if (expiresAtUtc.HasValue && DateTime.UtcNow > expiresAtUtc.Value)
            return null;

        return token;
    }

    private sealed record UserRolePageRow(Guid RoleId, string Name);

    private sealed record UserPageRow(
        Guid Id,
        string Username,
        string Email,
        string UserType,
        string Status,
        DateTime? LockedUntil,
        DateTime CreatedAt,
        DateTime? LastPasswordChangedAt,
        DateTime? PasswordExpiresAt,
        int FailedLoginAttempts,
        bool HasPendingInvitation,
        DateTime? InvitationExpiresAt,
        string? InvitationToken,
        Guid? ExternalReferenceId,
        List<UserRolePageRow> Roles);
}
