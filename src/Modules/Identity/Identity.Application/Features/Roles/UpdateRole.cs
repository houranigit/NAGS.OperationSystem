using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

public sealed record UpdateRoleCommand(Guid Id, string Name, string? Description) : ICommand;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class UpdateRoleCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var access = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Roles.Update,
                now,
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
                    "System roles cannot be modified.",
                    "Identity.Role.SystemProtected");
            }

            var normalized = request.Name.Trim().ToUpperInvariant();
            var duplicate = await db.Roles.AnyAsync(
                candidate =>
                    candidate.NormalizedName == normalized &&
                    candidate.Id != role.Id,
                cancellationToken);
            if (duplicate)
            {
                return Error.Conflict(
                    "A role with this name already exists.",
                    "Identity.Role.DuplicateName");
            }

            var result = role.Update(request.Name, request.Description, now);
            if (result.IsFailure)
                return result.Error;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
    }
}
