using System.Text;
using BuildingBlocks.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Shouldly;

namespace MasterData.IntegrationTests;

public sealed class LocalFileStorageTests
{
    [Fact]
    public async Task Open_and_delete_ignore_keys_that_escape_the_storage_root()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"ops-storage-parent-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "root");
        var outside = Path.Combine(parent, "outside.txt");

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(outside, "do-not-touch");

        try
        {
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = root }));

            var opened = await storage.OpenAsync("../outside.txt");
            await storage.DeleteAsync("../outside.txt");

            opened.ShouldBeNull();
            File.Exists(outside).ShouldBeTrue();
            (await File.ReadAllTextAsync(outside)).ShouldBe("do-not-touch");
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Save_writes_generated_keys_under_the_storage_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ops-storage-root-{Guid.NewGuid():N}");

        try
        {
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = root }));
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("logo"));

            var stored = await storage.SaveAsync("../bad-container", "logo.png", "image/png", content);
            await using var opened = await storage.OpenAsync(stored.StorageKey);
            using var reader = new StreamReader(opened!);

            stored.StorageKey.ShouldStartWith("bad-container/");
            (await reader.ReadToEndAsync()).ShouldBe("logo");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
