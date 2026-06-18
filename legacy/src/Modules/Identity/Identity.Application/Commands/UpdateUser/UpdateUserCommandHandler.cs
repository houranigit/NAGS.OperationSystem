using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Enumerations;
using Identity.Domain.ValueObjects;

namespace Identity.Application.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository)
    : ICommandHandler<UpdateUserCommand>
{
    public async Task<Result> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithRolesAsync(UserId.From(request.Id), cancellationToken);
        if (user is null) return Error.NotFound("User was not found.");

        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure) return emailResult.Error;

        // ChangeEmail returns Conflict if the value is unchanged — guard that branch so an
        // edit that only flips IsActive / roles still saves cleanly.
        if (user.Email != emailResult.Value)
        {
            var existing = await userRepository.GetByEmailAsync(emailResult.Value.Value, cancellationToken);
            if (existing is not null && existing.Id != user.Id)
                return Error.Conflict($"A user with email '{request.Email}' already exists.");

            var changeEmail = user.ChangeEmail(emailResult.Value);
            if (changeEmail.IsFailure) return changeEmail.Error;
        }

        // Activation flips are only meaningful for non-pending users — a PendingActivation
        // account must finish onboarding (set password via the invite link) before an admin
        // can change its IsActive state. We silently skip the toggle in that case so the
        // dialog can still save other edits (email/roles) for invited users.
        if (user.Status != UserStatus.PendingActivation)
        {
            if (request.IsActive && !user.IsActive)
            {
                var activate = user.Activate();
                if (activate.IsFailure) return activate.Error;
            }
            else if (!request.IsActive && user.IsActive)
            {
                var deactivate = user.Deactivate();
                if (deactivate.IsFailure) return deactivate.Error;
            }
        }

        var desiredRoleIds = (request.RoleIds ?? []).Select(RoleId.From).ToHashSet();
        if (desiredRoleIds.Count > 0)
        {
            var existingRoles = await roleRepository.GetByIdsAsync(desiredRoleIds, cancellationToken);
            var existingRoleIds = existingRoles.Select(r => r.Id).ToHashSet();
            var missingRoleIds = desiredRoleIds.Except(existingRoleIds).ToList();
            if (missingRoleIds.Count > 0)
                return Error.Validation("One or more selected roles no longer exist. Refresh the page and try again.");
        }

        var currentRoleIds = user.Roles.Select(r => r.RoleId).ToHashSet();

        foreach (var toRemove in currentRoleIds.Except(desiredRoleIds))
        {
            var rem = user.RemoveRole(toRemove);
            if (rem.IsFailure) return rem.Error;
        }

        foreach (var toAdd in desiredRoleIds.Except(currentRoleIds))
        {
            var add = user.AssignRole(toAdd);
            if (add.IsFailure) return add.Error;
        }

        userRepository.Update(user);
        return Result.Success();
    }
}
