using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

public sealed record CreateRoleCommand(string Name, string? Description, UserType CompatibleUserType, IReadOnlyList<string> Permissions) : ICommand<Guid>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.CompatibleUserType).IsInEnum();
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class CreateRoleCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    TimeProvider timeProvider)
    : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var createAccess = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Roles.Create,
                now,
                cancellationToken);
            if (createAccess.IsFailure)
                return createAccess.Error;

            // The endpoint requires both capabilities because creating a role also establishes its
            // permission grant. Recheck each claim against the same live account after taking the lock.
            var permissionAccess = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Roles.ManagePermissions,
                now,
                cancellationToken);
            if (permissionAccess.IsFailure)
                return permissionAccess.Error;

            var normalized = request.Name.Trim().ToUpperInvariant();
            var exists = await db.Roles.AnyAsync(
                role => role.NormalizedName == normalized,
                cancellationToken);
            if (exists)
            {
                return Error.Conflict(
                    "A role with this name already exists.",
                    "Identity.Role.DuplicateName");
            }

            var permissionCheck = RolePermissionValidator.Validate(
                request.Permissions,
                request.CompatibleUserType,
                permissions);
            if (permissionCheck.IsFailure)
                return permissionCheck.Error;

            var roleResult = Role.Create(
                request.Name,
                request.Description,
                request.Permissions,
                request.CompatibleUserType,
                now);
            if (roleResult.IsFailure)
                return roleResult.Error;

            var delegationAccess = RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(
                userContext,
                permissionAccess.Value,
                roleResult.Value,
                isCurrentRole: false);
            if (delegationAccess.IsFailure)
                return delegationAccess.Error;

            db.Roles.Add(roleResult.Value);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return roleResult.Value.Id;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
    }
}
