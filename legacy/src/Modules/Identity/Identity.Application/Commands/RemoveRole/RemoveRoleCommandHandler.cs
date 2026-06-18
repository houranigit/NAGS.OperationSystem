using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;

namespace Identity.Application.Commands.RemoveRole;

public sealed class RemoveRoleCommandHandler(
    IUserRepository userRepository)
    : ICommandHandler<RemoveRoleCommand>
{
    public async Task<Result> Handle(
        RemoveRoleCommand command,
        CancellationToken cancellationToken)
    {
        var userId = UserId.From(command.UserId);
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.");

        var roleId = RoleId.From(command.RoleId);
        var result = user.RemoveRole(roleId);
        if (!result.IsSuccess)
            return result.Error;

        userRepository.Update(user);
        return Result.Success();
    }
}
