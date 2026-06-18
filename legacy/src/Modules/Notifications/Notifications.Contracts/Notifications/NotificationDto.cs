namespace Notifications.Contracts.Notifications;

/// <summary>
/// Read-model returned by the inbox API and pushed live by the SignalR hub.
/// <c>PayloadJson</c> is opaque to the transport — clients parse it based on
/// <see cref="Kind"/> to deep-link into the right destination screen.
/// </summary>
public sealed record NotificationDto(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    string PayloadJson,
    bool IsRead,
    DateTime CreatedAt,
    DateTime? ReadAt);
