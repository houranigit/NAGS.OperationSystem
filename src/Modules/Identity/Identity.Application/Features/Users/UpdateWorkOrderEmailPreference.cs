using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Users;

public sealed record UpdateWorkOrderEmailPreferenceCommand(bool Enabled) : ICommand;

public sealed class UpdateWorkOrderEmailPreferenceCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<UpdateWorkOrderEmailPreferenceCommand>
{
    public async Task<Result> Handle(UpdateWorkOrderEmailPreferenceCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.Unauthorized();

        var result = user.SetWorkOrderEmailPreference(request.Enabled, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
