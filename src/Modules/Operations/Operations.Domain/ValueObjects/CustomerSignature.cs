using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Metadata for a customer signature whose bytes are held in file storage.</summary>
public sealed class CustomerSignature : ValueObject
{
    private CustomerSignature() { }

    public string FileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public DateTimeOffset SignedAtUtc { get; private set; }

    public static Result<CustomerSignature> Create(string? fileName, string? storageKey, string? contentType,
        long sizeBytes, DateTimeOffset signedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Error.Validation("Customer signature file name is required.", "Operations.CustomerSignature.FileNameRequired");
        if (string.IsNullOrWhiteSpace(storageKey))
            return Error.Validation("Customer signature storage key is required.", "Operations.CustomerSignature.StorageKeyRequired");
        if (string.IsNullOrWhiteSpace(contentType))
            return Error.Validation("Customer signature content type is required.", "Operations.CustomerSignature.ContentTypeRequired");
        if (storageKey.Trim().Length > 500)
            return Error.Validation("Customer signature storage key must be at most 500 characters.", "Operations.CustomerSignature.StorageKeyTooLong");
        if (contentType.Trim().Length > 100)
            return Error.Validation("Customer signature content type must be at most 100 characters.", "Operations.CustomerSignature.ContentTypeTooLong");
        if (sizeBytes <= 0)
            return Error.Validation("Customer signature size must be greater than zero.", "Operations.CustomerSignature.SizeInvalid");
        if (signedAtUtc == default)
            return Error.Validation("Customer signing time is required.", "Operations.CustomerSignature.SignedAtRequired");

        var normalizedFileName = Path.GetFileName(fileName.Trim().Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(normalizedFileName))
            return Error.Validation("Customer signature file name is required.", "Operations.CustomerSignature.FileNameRequired");

        return new CustomerSignature
        {
            FileName = normalizedFileName.Length <= 255 ? normalizedFileName : normalizedFileName[..255],
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
            SignedAtUtc = signedAtUtc.ToUniversalTime()
        };
    }

    internal CustomerSignature Copy() => Create(FileName, StorageKey, ContentType, SizeBytes, SignedAtUtc).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FileName;
        yield return StorageKey;
        yield return ContentType;
        yield return SizeBytes;
        yield return SignedAtUtc;
    }
}
