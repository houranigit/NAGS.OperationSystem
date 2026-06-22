namespace BuildingBlocks.Application.Abstractions;

/// <summary>Metadata returned after storing a binary blob. Persisted by callers; bytes are not.</summary>
public sealed record StoredFile(string StorageKey, string ContentType, long SizeBytes);

/// <summary>
/// Binary file storage behind a swappable abstraction (local filesystem in v1.0.0, cloud object
/// storage later). The database stores only the returned metadata/key, never the bytes.
/// </summary>
public interface IFileStorage
{
    public Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content, CancellationToken cancellationToken = default);

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default);

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
