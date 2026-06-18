using BuildingBlocks.Application.Abstractions.Commands;

namespace Notifications.Application.Features.RevokeDeviceToken;

/// <summary>
/// Revokes a previously-registered device token for the authenticated user. Used on
/// logout (mobile DELETE /api/notifications/me/devices/{token}) and when FCM reports the
/// token as Unregistered. The row is soft-revoked (RevokedAt set) for audit; future fan-out
/// skips it.
/// </summary>
public sealed record RevokeDeviceTokenCommand(Guid UserId, string Token) : ICommand;
