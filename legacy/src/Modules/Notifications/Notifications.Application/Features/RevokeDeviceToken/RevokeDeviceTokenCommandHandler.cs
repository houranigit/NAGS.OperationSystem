using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Notifications.Domain.Aggregates.DeviceToken;

namespace Notifications.Application.Features.RevokeDeviceToken;

public sealed class RevokeDeviceTokenCommandHandler(IDeviceTokenRepository repository)
    : ICommandHandler<RevokeDeviceTokenCommand>
{
    public async Task<Result> Handle(RevokeDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Error.Validation("User id is required.");
        if (string.IsNullOrWhiteSpace(request.Token))
            return Error.Validation("Device token is required.");

        var existing = await repository.GetByUserAndTokenAsync(request.UserId, request.Token.Trim(), cancellationToken);
        // Idempotent: a missing or already-revoked token is a no-op success.
        if (existing is null) return Result.Success();

        existing.Revoke(DateTime.UtcNow);
        repository.Update(existing);
        return Result.Success();
    }
}
