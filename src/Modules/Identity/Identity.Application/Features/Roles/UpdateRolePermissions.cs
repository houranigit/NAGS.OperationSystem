using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

public sealed record UpdateRolePermissionsCommand(Guid Id, IReadOnlyList<string> Permissions) : ICommand;

public sealed class UpdateRolePermissionsCommandValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class UpdateRolePermissionsCommandHandler(IIdentityDbContext db, IPermissionRegistry permissions, TimeProvider timeProvider)
    : ICommandHandler<UpdateRolePermissionsCommand>
{
    public async Task<Result> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (role is null)
            return Error.NotFound("Role not found.", "Identity.Role.NotFound");

        if (role.IsSystem)
            return Error.Conflict("System role permissions cannot be modified.", "Identity.Role.SystemProtected");

        var permissionCheck = RolePermissionValidator.Validate(request.Permissions, role.CompatibleUserType, permissions);
        if (permissionCheck.IsFailure)
            return permissionCheck.Error;

        var now = timeProvider.GetUtcNow();
        var result = role.SetPermissions(request.Permissions, now);
        if (result.IsFailure)
            return result.Error;

        // Everyone holding this role now has a different permission set; rotate their stamps and
        // revoke their sessions so outstanding tokens carrying the old permissions stop working.
        var affectedUsers = await db.Users
            .Where(u => u.RoleId == role.Id && u.Status == UserStatus.Active)
            .ToListAsync(cancellationToken);
        var affectedUserIds = affectedUsers.Select(u => u.Id).ToList();

        foreach (var user in affectedUsers)
            user.RotateSecurityStamp(now);

        if (affectedUserIds.Count > 0)
        {
            var sessions = await db.Sessions
                .Where(s => affectedUserIds.Contains(s.UserId) && s.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
                session.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
