using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Attachment metadata; binary content is held in file storage.</summary>
public sealed class AttachmentDetails : ValueObject
{
    private AttachmentDetails() { }

    public string FileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public AttachmentKind Kind { get; private set; }

    public static Result<AttachmentDetails> Create(string? fileName, string? storageKey, string? contentType, long sizeBytes,
        AttachmentKind kind)
    {
        if (!Enum.IsDefined(kind))
            return Error.Validation("Attachment kind is invalid.", "Operations.AttachmentDetails.KindInvalid");
        if (string.IsNullOrWhiteSpace(fileName))
            return Error.Validation("Attachment file name is required.", "Operations.AttachmentDetails.FileNameRequired");
        if (string.IsNullOrWhiteSpace(storageKey))
            return Error.Validation("Attachment storage key is required.", "Operations.AttachmentDetails.StorageKeyRequired");
        if (string.IsNullOrWhiteSpace(contentType))
            return Error.Validation("Attachment content type is required.", "Operations.AttachmentDetails.ContentTypeRequired");
        if (storageKey.Trim().Length > 500)
            return Error.Validation("Attachment storage key must be at most 500 characters.", "Operations.AttachmentDetails.StorageKeyTooLong");
        if (contentType.Trim().Length > 100)
            return Error.Validation("Attachment content type must be at most 100 characters.", "Operations.AttachmentDetails.ContentTypeTooLong");
        if (sizeBytes <= 0)
            return Error.Validation("Attachment size must be greater than zero.", "Operations.AttachmentDetails.SizeInvalid");

        var normalizedFileName = Path.GetFileName(fileName.Trim().Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(normalizedFileName))
            return Error.Validation("Attachment file name is required.", "Operations.AttachmentDetails.FileNameRequired");

        return new AttachmentDetails
        {
            FileName = normalizedFileName.Length <= 255 ? normalizedFileName : normalizedFileName[..255],
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
            Kind = kind
        };
    }

    internal AttachmentDetails Copy() => Create(FileName, StorageKey, ContentType, SizeBytes, Kind).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FileName;
        yield return StorageKey;
        yield return ContentType;
        yield return SizeBytes;
        yield return Kind;
    }
}
