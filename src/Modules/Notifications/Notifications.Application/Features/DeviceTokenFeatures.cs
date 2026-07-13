using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Domain.Devices;

namespace Notifications.Application.Features;

public sealed record RegisterDeviceTokenCommand(
    string Token,
    DevicePlatform Platform,
    string DeviceId,
    string? Locale,
    string? AppVersion) : ICommand;

public sealed class RegisterDeviceTokenCommandHandler(INotificationsDbContext db, IUserContext user, TimeProvider timeProvider)
    : ICommandHandler<RegisterDeviceTokenCommand>
{
    public async Task<Result> Handle(RegisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");
        if (string.IsNullOrWhiteSpace(request.Token))
            return Error.Validation("Device token is required.", "Notifications.Device.TokenRequired");

        var token = request.Token.Trim();
        var tokenHash = DeviceToken.ComputeTokenHash(token);
        var now = timeProvider.GetUtcNow();
        var existing = await db.DeviceTokens.FirstOrDefaultAsync(
            t => t.DeviceId == request.DeviceId || t.TokenHash == tokenHash,
            cancellationToken);
        if (existing is not null)
        {
            var refresh = existing.Refresh(userId, token, request.Platform, request.DeviceId, request.Locale, request.AppVersion, now);
            if (refresh.IsFailure)
                return refresh.Error;
        }
        else
        {
            var created = DeviceToken.Register(userId, token, request.Platform, request.DeviceId, request.Locale, request.AppVersion, now);
            if (created.IsFailure)
                return created.Error;
            db.DeviceTokens.Add(created.Value);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // App startup and Firebase's onNewToken callback can race. The unique token/device
            // indexes arbitrate; reload the winning registration and apply this request once.
            foreach (var entry in db.DeviceTokens.Local.ToList())
                db.DeviceTokens.Entry(entry).State = EntityState.Detached;

            existing = await db.DeviceTokens.FirstOrDefaultAsync(
                t => t.DeviceId == request.DeviceId || t.TokenHash == tokenHash,
                cancellationToken);
            if (existing is null)
                throw;

            var refresh = existing.Refresh(userId, token, request.Platform, request.DeviceId, request.Locale, request.AppVersion, now);
            if (refresh.IsFailure)
                return refresh.Error;
            await db.SaveChangesAsync(cancellationToken);
        }
        return Result.Success();
    }
}

public sealed record RevokeDeviceTokenCommand(string Token) : ICommand;

public sealed class RevokeDeviceTokenCommandHandler(INotificationsDbContext db, IUserContext user, TimeProvider timeProvider)
    : ICommandHandler<RevokeDeviceTokenCommand>
{
    public async Task<Result> Handle(RevokeDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");
        if (string.IsNullOrWhiteSpace(request.Token))
            return Error.Validation("Device token is required.", "Notifications.Device.TokenRequired");

        var token = request.Token.Trim();
        var tokenHash = DeviceToken.ComputeTokenHash(token);
        var existing = await db.DeviceTokens.FirstOrDefaultAsync(
            t => t.UserId == userId && t.TokenHash == tokenHash && t.RevokedAtUtc == null,
            cancellationToken);
        if (existing is null)
            return Result.Success();

        existing.Revoke(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
