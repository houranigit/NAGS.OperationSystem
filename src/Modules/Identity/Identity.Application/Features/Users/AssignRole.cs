using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
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

public sealed class AssignRoleCommandHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : ICommandHandler<AssignRoleCommand>
{
    public async Task<Result> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
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

        var result = user.AssignRole(request.RoleId, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
