using Notifications.Domain.Devices;

namespace Notifications.Api.Endpoints;

public sealed record RegisterDeviceTokenRequest(
    string Token,
    DevicePlatform Platform = DevicePlatform.Android,
    string DeviceId = "",
    string? Locale = null,
    string? AppVersion = null);

public sealed record RevokeDeviceTokenRequest(string Token);
