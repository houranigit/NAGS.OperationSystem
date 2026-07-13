namespace Notifications.Contracts;

/// <summary>Persisted user-facing notification returned by the inbox API and pushed over SignalR.</summary>
public sealed record NotificationDto(
    Guid Id,
    string Kind,
    string TitleEn,
    string BodyEn,
    string TitleAr,
    string BodyAr,
    IReadOnlyDictionary<string, string> Payload,
    bool IsRead,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

public sealed record UnreadNotificationCountDto(int Count);
