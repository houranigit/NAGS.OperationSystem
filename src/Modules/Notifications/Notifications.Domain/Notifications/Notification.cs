using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace Notifications.Domain.Notifications;

/// <summary>Authoritative inbox/history entry for one portal user.</summary>
public sealed class Notification : AggregateRoot<Guid>
{
    private Notification() { }

    public Guid RecipientUserId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string TitleEn { get; private set; } = null!;
    public string BodyEn { get; private set; } = null!;
    public string TitleAr { get; private set; } = null!;
    public string BodyAr { get; private set; } = null!;
    public string PayloadJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public bool IsRead => ReadAtUtc is not null;
    public bool IsArchived => ArchivedAtUtc is not null;

    public static Result<Notification> Create(
        Guid id,
        Guid recipientUserId,
        string kind,
        string titleEn,
        string bodyEn,
        string titleAr,
        string bodyAr,
        string? payloadJson,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
            return Error.Validation("Notification id is required.", "Notifications.Notification.IdRequired");
        if (recipientUserId == Guid.Empty)
            return Error.Validation("Recipient user id is required.", "Notifications.Notification.RecipientRequired");
        if (string.IsNullOrWhiteSpace(kind) || kind.Trim().Length > 64)
            return Error.Validation("A valid notification kind is required.", "Notifications.Notification.KindInvalid");

        var localizedCopy = ValidateLocalizedCopy(titleEn, bodyEn, titleAr, bodyAr);
        if (localizedCopy.IsFailure)
            return localizedCopy.Error;

        return new Notification
        {
            Id = id,
            RecipientUserId = recipientUserId,
            Kind = kind.Trim(),
            TitleEn = titleEn.Trim(),
            BodyEn = bodyEn.Trim(),
            TitleAr = titleAr.Trim(),
            BodyAr = bodyAr.Trim(),
            PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            CreatedAtUtc = now
        };
    }

    public Result MarkAsRead(DateTimeOffset now)
    {
        ReadAtUtc ??= now;
        return Result.Success();
    }

    public Result Archive(DateTimeOffset now)
    {
        ArchivedAtUtc ??= now;
        return Result.Success();
    }

    public Result MarkDelivered(DateTimeOffset now)
    {
        DeliveredAtUtc ??= now;
        return Result.Success();
    }

    private static Result ValidateLocalizedCopy(string titleEn, string bodyEn, string titleAr, string bodyAr)
    {
        if (string.IsNullOrWhiteSpace(titleEn) || titleEn.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(titleAr) || titleAr.Trim().Length > 200)
        {
            return Error.Validation("Localized notification titles are required and must not exceed 200 characters.",
                "Notifications.Notification.TitleInvalid");
        }

        if (string.IsNullOrWhiteSpace(bodyEn) || bodyEn.Trim().Length > 500 ||
            string.IsNullOrWhiteSpace(bodyAr) || bodyAr.Trim().Length > 500)
        {
            return Error.Validation("Localized notification bodies are required and must not exceed 500 characters.",
                "Notifications.Notification.BodyInvalid");
        }

        return Result.Success();
    }
}
