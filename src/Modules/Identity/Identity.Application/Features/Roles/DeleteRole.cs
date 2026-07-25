using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

public sealed record DeleteRoleCommand(Guid Id) : ICommand;

public sealed class DeleteRoleCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    TimeProvider timeProvider)
    : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var access = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Roles.Delete,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (access.IsFailure)
                return access.Error;

            var role = await db.Roles.FirstOrDefaultAsync(
                candidate => candidate.Id == request.Id,
                cancellationToken);
            if (role is null)
                return Error.NotFound("Role not found.", "Identity.Role.NotFound");

            if (role.IsSystem)
            {
                return Error.Conflict(
                    "System roles cannot be deleted.",
                    "Identity.Role.SystemProtected");
            }

            var inUse = await db.Users.AnyAsync(
                user => user.RoleId == role.Id,
                cancellationToken);
            if (inUse)
                return RoleInUse();

            db.Roles.Remove(role);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException)
        {
            // A user can still be assigned by a legacy/external writer that does not participate
            // in the access-management lock. The restrictive FK is the final arbiter.
            return RoleInUse();
        }
    }

    private static Error RoleInUse() =>
        Error.Conflict(
            "Cannot delete a role that is assigned to users.",
            "Identity.Role.InUse");
}
