using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Users;

public sealed record UpdateUserCommand(Guid Id, string DisplayName) : ICommand;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(150);
    }
}

public sealed class UpdateUserCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateUserCommand>
{
    public async Task<Result> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var liveActorRole = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Users.Update,
                now,
                cancellationToken);
            if (liveActorRole.IsFailure)
                return liveActorRole.Error;

            var user = await db.Users.FirstOrDefaultAsync(
                candidate => candidate.Id == request.Id,
                cancellationToken);
            if (user is null)
                return Error.NotFound("User not found.", "Identity.User.NotFound");

            var result = user.UpdateProfile(request.DisplayName, now);
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
