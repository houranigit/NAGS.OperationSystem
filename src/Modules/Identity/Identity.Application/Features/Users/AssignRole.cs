using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Users;

public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : ICommand;

public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class AssignRoleCommandHandler(IIdentityDbContext db, IUserContext userContext, TimeProvider timeProvider)
    : ICommandHandler<AssignRoleCommand>
{
    public async Task<Result> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        var roleAssignmentAccess = RoleAssignmentAuthorization.EnsureCanAssignRole(userContext);
        if (roleAssignmentAccess.IsFailure)
            return roleAssignmentAccess.Error;

        if (userContext.UserId == request.UserId)
            return Error.Conflict("You cannot change your own role.", "Identity.User.CannotAssignRoleSelf");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
            return Error.Validation("The selected role does not exist.", "Identity.User.RoleNotFound");

        if (role.CompatibleUserType != user.UserType)
            return Error.Conflict(
                $"Role '{role.Name}' is not compatible with this account's type ({user.UserType}).",
                "Identity.User.IncompatibleRole");

        var delegationAccess = RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(userContext, role);
        if (delegationAccess.IsFailure)
            return delegationAccess.Error;

        var now = timeProvider.GetUtcNow();
        var result = user.AssignRole(request.RoleId, now);
        if (result.IsFailure)
            return result.Error;

        // Revoke existing sessions so the old role's refresh tokens cannot be used.
        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
