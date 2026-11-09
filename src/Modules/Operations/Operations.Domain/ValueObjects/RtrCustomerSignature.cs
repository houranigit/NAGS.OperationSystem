using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Return-to-ramp signature metadata, including its file name and signing time.</summary>
public sealed class RtrCustomerSignature : ValueObject
{
    private RtrCustomerSignature() { }

    public string FileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public DateTimeOffset SignedAtUtc { get; private set; }

    public static Result<RtrCustomerSignature> Create(string? fileName, string? storageKey,
        string? contentType, long sizeBytes, DateTimeOffset signedAtUtc)
    {
        var details = AttachmentDetails.Create(fileName, storageKey, contentType, sizeBytes, AttachmentKind.Image);
        if (details.IsFailure)
            return details.Error;
        if (signedAtUtc == default)
            return Error.Validation("Customer signing time is required.", "Operations.RtrCustomerSignature.SignedAtRequired");

        return new RtrCustomerSignature
        {
            FileName = details.Value.FileName,
            StorageKey = details.Value.StorageKey,
            ContentType = details.Value.ContentType,
            SizeBytes = details.Value.SizeBytes,
            SignedAtUtc = signedAtUtc.ToUniversalTime()
        };
    }

    internal RtrCustomerSignature Copy() => Create(FileName, StorageKey, ContentType, SizeBytes, SignedAtUtc).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FileName;
        yield return StorageKey;
        yield return ContentType;
        yield return SizeBytes;
        yield return SignedAtUtc;
    }
}
