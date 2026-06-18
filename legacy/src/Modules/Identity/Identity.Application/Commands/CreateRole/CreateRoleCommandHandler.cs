using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.Role;

namespace Identity.Application.Commands.CreateRole;

public sealed class CreateRoleCommandHandler(
    IRoleRepository roleRepository)
    : ICommandHandler<CreateRoleCommand, CreateRoleResult>
{
    public async Task<Result<CreateRoleResult>> Handle(
        CreateRoleCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Check name uniqueness
        var existing = await roleRepository.GetByNameAsync(command.Name, cancellationToken);
        if (existing is not null)
            return Error.Conflict($"A role named '{command.Name}' already exists.");

        // 2. Create role
        var roleResult = Role.Create(command.Name, command.Description);
        if (!roleResult.IsSuccess)
            return roleResult.Error;

        var role = roleResult.Value;

        // 3. Add permissions
        foreach (var permission in command.PermissionCodes)
        {
            var addResult = role.AddPermission(permission);
            if (!addResult.IsSuccess)
                return addResult.Error;
        }

        roleRepository.Add(role);
        return new CreateRoleResult(role.Id.Value);
    }
}
