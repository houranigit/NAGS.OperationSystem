using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;

namespace Identity.Application.Commands.AssignRole;

public sealed class AssignRoleCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository)
    : ICommandHandler<AssignRoleCommand>
{
    public async Task<Result> Handle(
        AssignRoleCommand command,
        CancellationToken cancellationToken)
    {
        var userId = UserId.From(command.UserId);
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.");

        var roleId = RoleId.From(command.RoleId);
        var role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
            return Error.NotFound("Role not found.");

        var result = user.AssignRole(roleId);
        if (!result.IsSuccess)
            return result.Error;

        userRepository.Update(user);
        return Result.Success();
    }
}
