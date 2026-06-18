using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Notifications.Domain.Aggregates.DeviceToken;

namespace Notifications.Application.Features.RegisterDeviceToken;

public sealed class RegisterDeviceTokenCommandHandler(IDeviceTokenRepository repository)
    : ICommandHandler<RegisterDeviceTokenCommand>
{
    public async Task<Result> Handle(RegisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Error.Validation("User id is required.");
        if (string.IsNullOrWhiteSpace(request.Token))
            return Error.Validation("Device token is required.");

        var trimmed = request.Token.Trim();
        var existing = await repository.GetByUserAndTokenAsync(request.UserId, trimmed, cancellationToken);

        var now = DateTime.UtcNow;
        if (existing is not null)
        {
            existing.Refresh(now);
            repository.Update(existing);
            return Result.Success();
        }

        var build = DeviceToken.Register(request.UserId, trimmed, request.Platform, now);
        if (build.IsFailure) return build.Error;

        repository.Add(build.Value);
        return Result.Success();
    }
}
