using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Metadata for a customer signature whose bytes are held in file storage.</summary>
public sealed class CustomerSignature : ValueObject
{
    private CustomerSignature() { }

    public string StorageKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    public static Result<CustomerSignature> Create(string? storageKey, string? contentType, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return Error.Validation("Customer signature storage key is required.", "Operations.CustomerSignature.StorageKeyRequired");
        if (string.IsNullOrWhiteSpace(contentType))
            return Error.Validation("Customer signature content type is required.", "Operations.CustomerSignature.ContentTypeRequired");
        if (sizeBytes <= 0)
            return Error.Validation("Customer signature size must be greater than zero.", "Operations.CustomerSignature.SizeInvalid");

        return new CustomerSignature
        {
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes
        };
    }

    internal CustomerSignature Copy() => Create(StorageKey, ContentType, SizeBytes).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return StorageKey;
        yield return ContentType;
        yield return SizeBytes;
    }
}
