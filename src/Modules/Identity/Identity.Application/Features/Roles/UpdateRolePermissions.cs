using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
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

public sealed class UpdateRolePermissionsCommandHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateRolePermissionsCommand>
{
    public async Task<Result> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (role is null)
            return Error.NotFound("Role not found.", "Identity.Role.NotFound");

        if (role.IsSystem)
            return Error.Conflict("System role permissions cannot be modified.", "Identity.Role.SystemProtected");

        var result = role.SetPermissions(request.Permissions, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
