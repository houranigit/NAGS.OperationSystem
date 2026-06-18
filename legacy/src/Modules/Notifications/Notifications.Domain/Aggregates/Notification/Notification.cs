using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace Notifications.Domain.Aggregates.Notification;

/// <summary>
/// User-facing inbox entry. Created by integration-event handlers (e.g. when a teammate
/// invites the user to a flight) and consumed by the portal bell + mobile inbox screen.
/// Body / payload are kept lightweight; clients deep-link on <see cref="Kind"/> +
/// <see cref="PayloadJson"/>.
/// </summary>
public sealed class Notification : AggregateRoot<NotificationId>
{
    private Notification() { }

    public Guid RecipientUserId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string PayloadJson { get; private set; } = "{}";
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    /// <summary>
    /// Soft-archive flag. The mobile inbox's "Clear all" action sets this for every
    /// non-archived row of the recipient; archived rows are filtered out of the inbox
    /// query and the unread-count query but kept in the database for audit / undo.
    /// </summary>
    public bool IsArchived { get; private set; }
    public DateTime? ArchivedAt { get; private set; }

    public static Result<Notification> Create(
        Guid recipientUserId,
        string kind,
        string title,
        string body,
        string? payloadJson,
        DateTime utcNow)
    {
        if (recipientUserId == Guid.Empty)
            return Error.Validation("Recipient user id is required.");
        if (string.IsNullOrWhiteSpace(kind))
            return Error.Validation("Kind is required.");
        if (string.IsNullOrWhiteSpace(title))
            return Error.Validation("Title is required.");
        if (string.IsNullOrWhiteSpace(body))
            return Error.Validation("Body is required.");

        return new Notification
        {
            Id = NotificationId.New(),
            RecipientUserId = recipientUserId,
            Kind = kind.Trim(),
            Title = title.Trim(),
            Body = body.Trim(),
            PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            IsRead = false,
            CreatedAt = utcNow,
            ReadAt = null,
            IsArchived = false,
            ArchivedAt = null,
        };
    }

    public Result MarkAsRead(DateTime utcNow)
    {
        if (IsRead)
            return Result.Success();

        IsRead = true;
        ReadAt = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Soft-deletes the notification. Idempotent — repeated calls are no-ops once
    /// archived. Inbox queries filter on <see cref="IsArchived"/> = false.
    /// </summary>
    public Result Archive(DateTime utcNow)
    {
        if (IsArchived)
            return Result.Success();

        IsArchived = true;
        ArchivedAt = utcNow;
        return Result.Success();
    }
}
