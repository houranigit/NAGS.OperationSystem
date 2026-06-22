using BuildingBlocks.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Storage;

/// <summary>
/// Local-filesystem implementation of <see cref="IFileStorage"/>. The storage key is
/// <c>{container}/{guid}{ext}</c>; callers persist only the key, never the bytes. Designed to be
/// swapped for cloud object storage without touching callers.
/// </summary>
public sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath);

    public async Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        var safeContainer = Sanitize(container);
        var ext = Path.GetExtension(fileName);
        var key = $"{safeContainer}/{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        var size = new FileInfo(fullPath).Length;
        return new StoredFile(key, contentType, size);
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(File.OpenRead(fullPath));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private static string Sanitize(string container) =>
        string.Concat(container.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')).ToLowerInvariant() is { Length: > 0 } s
            ? s
            : "misc";
}
