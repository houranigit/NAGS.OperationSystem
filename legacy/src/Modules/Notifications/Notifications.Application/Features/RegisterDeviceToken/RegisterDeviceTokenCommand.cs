using BuildingBlocks.Application.Abstractions.Commands;
using Notifications.Domain.Aggregates.DeviceToken;

namespace Notifications.Application.Features.RegisterDeviceToken;

/// <summary>
/// Upsert a mobile device's FCM token for the authenticated user. Idempotent: re-sending
/// the same (UserId, Token) bumps LastSeenAt and clears any RevokedAt so push fan-out
/// resumes after a token rotation or reinstall.
/// </summary>
public sealed record RegisterDeviceTokenCommand(
    Guid UserId,
    string Token,
    DevicePlatform Platform) : ICommand;
