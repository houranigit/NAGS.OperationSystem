namespace OperationsSystem.Blazor.Client.Api;

/// <summary>
/// One persisted notification returned by the Notifications module and pushed over SignalR.
/// Both translations travel with the record so live delivery follows the portal's active locale.
/// </summary>
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

public sealed record UnreadNotificationCount(int Count);
