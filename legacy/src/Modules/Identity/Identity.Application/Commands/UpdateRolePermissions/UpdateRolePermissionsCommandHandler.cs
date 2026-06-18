using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.Role;

namespace Identity.Application.Commands.UpdateRolePermissions;

public sealed class UpdateRolePermissionsCommandHandler(
    IRoleRepository roleRepository)
    : ICommandHandler<UpdateRolePermissionsCommand>
{
    public async Task<Result> Handle(
        UpdateRolePermissionsCommand command,
        CancellationToken cancellationToken)
    {
        var roleId = RoleId.From(command.RoleId);
        var role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
            return Error.NotFound("Role not found.");

        // Replace all permissions
        role.ClearPermissions();
        foreach (var permission in command.PermissionCodes)
        {
            var addResult = role.AddPermission(permission);
            if (!addResult.IsSuccess)
                return addResult.Error;
        }

        roleRepository.Update(role);
        return Result.Success();
    }
}
