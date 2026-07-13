using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using System.Security.Cryptography;
using System.Text;

namespace Notifications.Domain.Devices;

/// <summary>A soft-revocable push token. Token ownership follows the latest authenticated registration.</summary>
public sealed class DeviceToken : AggregateRoot<Guid>
{
    private DeviceToken() { }

    public Guid UserId { get; private set; }
    public string Token { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public DevicePlatform Platform { get; private set; }
    public string DeviceId { get; private set; } = null!;
    public string Locale { get; private set; } = "en";
    public string? AppVersion { get; private set; }
    public DateTimeOffset RegisteredAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    public static Result<DeviceToken> Register(
        Guid userId,
        string token,
        DevicePlatform platform,
        string deviceId,
        string? locale,
        string? appVersion,
        DateTimeOffset now)
    {
        var validation = Validate(userId, token, deviceId, appVersion);
        if (validation.IsFailure)
            return validation.Error;

        return new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token.Trim(),
            TokenHash = ComputeTokenHash(token),
            Platform = platform,
            DeviceId = deviceId.Trim(),
            Locale = NormalizeLocale(locale),
            AppVersion = Normalize(appVersion),
            RegisteredAtUtc = now,
            LastSeenAtUtc = now
        };
    }

    public Result Refresh(
        Guid userId,
        string token,
        DevicePlatform platform,
        string deviceId,
        string? locale,
        string? appVersion,
        DateTimeOffset now)
    {
        var validation = Validate(userId, token, deviceId, appVersion);
        if (validation.IsFailure)
            return validation.Error;

        UserId = userId;
        Token = token.Trim();
        TokenHash = ComputeTokenHash(token);
        Platform = platform;
        DeviceId = deviceId.Trim();
        Locale = NormalizeLocale(locale);
        AppVersion = Normalize(appVersion);
        LastSeenAtUtc = now;
        RevokedAtUtc = null;
        return Result.Success();
    }

    public Result Revoke(DateTimeOffset now)
    {
        RevokedAtUtc ??= now;
        return Result.Success();
    }

    private static Result Validate(Guid userId, string token, string? deviceId, string? appVersion)
    {
        if (userId == Guid.Empty)
            return Error.Validation("User id is required.", "Notifications.Device.UserRequired");
        if (string.IsNullOrWhiteSpace(token) || token.Trim().Length > 4096)
            return Error.Validation("A valid device token is required.", "Notifications.Device.TokenInvalid");
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Trim().Length > 200)
            return Error.Validation("A valid device id is required.", "Notifications.Device.DeviceIdInvalid");
        if (appVersion?.Trim().Length > 50)
            return Error.Validation("App version must not exceed 50 characters.", "Notifications.Device.AppVersionInvalid");
        return Result.Success();
    }

    private static string NormalizeLocale(string? locale)
    {
        var language = locale?.Trim().Split('-', '_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
    }

    public static string ComputeTokenHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
