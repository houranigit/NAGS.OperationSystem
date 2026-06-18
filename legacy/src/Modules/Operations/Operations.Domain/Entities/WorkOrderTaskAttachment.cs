using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Enumerations;

namespace Operations.Domain.Entities;

/// <summary>
/// Inline binary attachment on a <see cref="WorkOrderTask"/>: image, voice memo, or
/// document. Stored as <c>varbinary(max)</c> directly on the task aggregate while we
/// remain on the byte[] storage strategy. Migrating to blob storage later only requires
/// replacing <see cref="Bytes"/> with a path/key — the rest of the model is stable.
/// </summary>
public sealed class WorkOrderTaskAttachment : Entity<Guid>
{
    public Guid TaskId { get; private set; }
    public TaskAttachmentKind Kind { get; private set; }
    public string ContentType { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public byte[] Bytes { get; private set; } = [];
    public int SizeBytes { get; private set; }
    public DateTimeOffset CapturedAt { get; private set; }

    // Per-kind soft caps. Voice is held to a lower cap because mobile records 30 s clips at
    // moderate bitrate (~64 kbps ⇒ ~240 KB) — a 2 MB ceiling leaves comfortable headroom for
    // higher-quality codecs without bloating the row.
    private const int MaxImageBytes = 5 * 1024 * 1024;        // 5 MB
    private const int MaxVoiceBytes = 2 * 1024 * 1024;        // 2 MB
    private const int MaxDocumentBytes = 10 * 1024 * 1024;    // 10 MB

    private WorkOrderTaskAttachment()
    {
    }

    internal static Result<WorkOrderTaskAttachment> Create(
        Guid taskId,
        TaskAttachmentKind kind,
        string? contentType,
        string? fileName,
        byte[]? bytes,
        DateTimeOffset capturedAt)
    {
        if (bytes is null || bytes.Length == 0)
            return Error.Validation("Attachment bytes are required.");
        if (string.IsNullOrWhiteSpace(contentType))
            return Error.Validation("Attachment content type is required.");
        if (string.IsNullOrWhiteSpace(fileName))
            return Error.Validation("Attachment file name is required.");
        if (fileName.Length > 200)
            return Error.Validation("Attachment file name must not exceed 200 characters.");

        var cap = kind switch
        {
            TaskAttachmentKind.Image => MaxImageBytes,
            TaskAttachmentKind.Voice => MaxVoiceBytes,
            TaskAttachmentKind.Document => MaxDocumentBytes,
            _ => MaxDocumentBytes
        };
        if (bytes.Length > cap)
            return Error.Validation($"Attachment exceeds the {cap / 1024 / 1024} MB size cap for {kind}.");

        return new WorkOrderTaskAttachment
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Kind = kind,
            ContentType = contentType.Trim(),
            FileName = fileName.Trim(),
            Bytes = bytes,
            SizeBytes = bytes.Length,
            CapturedAt = capturedAt
        };
    }
}
