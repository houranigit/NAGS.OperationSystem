using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

public sealed record DeleteRoleCommand(Guid Id) : ICommand;

public sealed class DeleteRoleCommandHandler(IIdentityDbContext db) : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (role is null)
            return Error.NotFound("Role not found.", "Identity.Role.NotFound");

        if (role.IsSystem)
            return Error.Conflict("System roles cannot be deleted.", "Identity.Role.SystemProtected");

        var inUse = await db.Users.AnyAsync(u => u.RoleId == role.Id, cancellationToken);
        if (inUse)
            return Error.Conflict("Cannot delete a role that is assigned to users.", "Identity.Role.InUse");

        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
