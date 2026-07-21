using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

/// <summary>
/// Atomically updates a role's profile and permission grant. This command backs the two-step role
/// editor; the narrower metadata-only and permissions-only commands remain available for callers
/// that hold just one of those capabilities.
/// </summary>
public sealed record UpdateRoleAndPermissionsCommand(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : ICommand;

public sealed class UpdateRoleAndPermissionsCommandValidator : AbstractValidator<UpdateRoleAndPermissionsCommand>
{
    public UpdateRoleAndPermissionsCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class UpdateRoleAndPermissionsCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    IPermissionRegistry permissions,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateRoleAndPermissionsCommand>
{
    public async Task<Result> Handle(UpdateRoleAndPermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (role is null)
            return Error.NotFound("Role not found.", "Identity.Role.NotFound");

        if (role.IsSystem)
            return Error.Conflict("System roles cannot be modified.", "Identity.Role.SystemProtected");

        var normalized = request.Name.Trim().ToUpperInvariant();
        var duplicate = await db.Roles.AnyAsync(
            r => r.NormalizedName == normalized && r.Id != role.Id,
            cancellationToken);
        if (duplicate)
            return Error.Conflict("A role with this name already exists.", "Identity.Role.DuplicateName");

        var permissionCheck = RolePermissionValidator.Validate(request.Permissions, role.CompatibleUserType, permissions);
        if (permissionCheck.IsFailure)
            return permissionCheck.Error;

        var permissionsChanged = !role.Permissions.ToHashSet(StringComparer.Ordinal)
            .SetEquals(request.Permissions);

        if (permissionsChanged && currentUser.UserId is { } currentUserId)
        {
            var isOwnRole = await db.Users.AnyAsync(
                user => user.Id == currentUserId && user.RoleId == role.Id && user.Status == UserStatus.Active,
                cancellationToken);
            if (isOwnRole)
                return Error.Conflict("You cannot modify permissions for your own role.", "Identity.Role.CannotModifyOwnPermissions");
        }

        var now = timeProvider.GetUtcNow();
        var updateResult = role.Update(request.Name, request.Description, now);
        if (updateResult.IsFailure)
            return updateResult.Error;

        if (permissionsChanged)
        {
            var permissionResult = role.SetPermissions(request.Permissions, now);
            if (permissionResult.IsFailure)
                return permissionResult.Error;

            var affectedUsers = await db.Users
                .Where(user => user.RoleId == role.Id && user.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            var affectedUserIds = affectedUsers.Select(user => user.Id).ToList();

            foreach (var user in affectedUsers)
                user.RotateSecurityStamp(now);

            if (affectedUserIds.Count > 0)
            {
                var sessions = await db.Sessions
                    .Where(session => affectedUserIds.Contains(session.UserId) && session.RevokedAtUtc == null)
                    .ToListAsync(cancellationToken);
                foreach (var session in sessions)
                    session.Revoke(now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
