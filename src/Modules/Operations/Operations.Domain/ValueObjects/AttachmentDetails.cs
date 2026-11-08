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

    public static Result<AttachmentDetails> Create(string? fileName, string? storageKey, string? contentType, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Error.Validation("Attachment file name is required.", "Operations.AttachmentDetails.FileNameRequired");
        if (string.IsNullOrWhiteSpace(storageKey))
            return Error.Validation("Attachment storage key is required.", "Operations.AttachmentDetails.StorageKeyRequired");
        if (string.IsNullOrWhiteSpace(contentType))
            return Error.Validation("Attachment content type is required.", "Operations.AttachmentDetails.ContentTypeRequired");
        if (sizeBytes <= 0)
            return Error.Validation("Attachment size must be greater than zero.", "Operations.AttachmentDetails.SizeInvalid");

        return new AttachmentDetails
        {
            FileName = fileName.Trim(),
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes
        };
    }

    internal AttachmentDetails Copy() => Create(FileName, StorageKey, ContentType, SizeBytes).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FileName;
        yield return StorageKey;
        yield return ContentType;
        yield return SizeBytes;
    }
}
