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

public sealed record UnreadNotificationCount(int Count, int? Invitations = null, int? Reminders = null, int? FlightUpdates = null)
{
    public int? ForCategory(string category) => category switch
    {
        NotificationCategories.Invitations => Invitations,
        NotificationCategories.Reminders => Reminders,
        NotificationCategories.FlightUpdates => FlightUpdates,
        _ => null
    };
}

public sealed record MarkNotificationCategoryReadRequest(string Category, DateTimeOffset ThroughCreatedAtUtc);

public sealed record NotificationCategoryReadDto(DateTimeOffset ReadAtUtc, int UnreadCount,
    int? Invitations = null, int? Reminders = null, int? FlightUpdates = null)
{
    public UnreadNotificationCount Counts => new(UnreadCount, Invitations, Reminders, FlightUpdates);
}
