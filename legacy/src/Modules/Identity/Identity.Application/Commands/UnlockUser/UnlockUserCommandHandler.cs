using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.User;

namespace Identity.Application.Commands.UnlockUser;

public sealed class UnlockUserCommandHandler(
    IUserRepository userRepository)
    : ICommandHandler<UnlockUserCommand>
{
    public async Task<Result> Handle(
        UnlockUserCommand command,
        CancellationToken cancellationToken)
    {
        var userId = UserId.From(command.UserId);
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.");

        var result = user.Unlock();
        if (!result.IsSuccess)
            return result.Error;

        userRepository.Update(user);
        return Result.Success();
    }
}
