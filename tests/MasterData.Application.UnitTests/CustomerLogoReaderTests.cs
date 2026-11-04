using BuildingBlocks.Application.Abstractions;
using MasterData.Contracts.Readers;
using MasterData.Domain.Customers;
using MasterData.Infrastructure.Persistence;
using MasterData.Infrastructure.Readers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MasterData.Application.UnitTests;

public sealed class CustomerLogoReaderTests
{
    [Fact]
    public async Task Missing_customer_or_logo_does_not_open_storage()
    {
        await using var db = NewDb();
        var customer = CustomerWithLogo(null);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var storage = new ReadOnlyStorage(null);
        var reader = new CustomerLogoReader(db, storage);

        (await reader.GetLogoAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        (await reader.GetLogoAsync(customer.Id, CancellationToken.None)).ShouldBeNull();

        storage.OpenedKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_stored_file_returns_no_logo()
    {
        await using var db = NewDb();
        var customer = CustomerWithLogo("customer-logos/missing.png");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var storage = new ReadOnlyStorage(null);

        (await new CustomerLogoReader(db, storage).GetLogoAsync(customer.Id, CancellationToken.None)).ShouldBeNull();

        storage.OpenedKeys.ShouldBe(["customer-logos/missing.png"]);
    }

    public static IEnumerable<object[]> RasterImages()
    {
        yield return [new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }, "image/png"];
        yield return [new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }, "image/jpeg"];
        yield return [new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0, 0, 0, 0, 0 }, "image/gif"];
        yield return [new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp"];
    }

    [Theory]
    [MemberData(nameof(RasterImages))]
    public async Task Raster_content_uses_its_signature_even_with_an_unknown_extension(byte[] content, string contentType)
    {
        await using var db = NewDb();
        var customer = CustomerWithLogo("customer-logos/upload.bin");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var file = new TrackedStream(content);
        var storage = new ReadOnlyStorage(file);
        using var cancellation = new CancellationTokenSource();

        var result = await new CustomerLogoReader(db, storage).GetLogoAsync(customer.Id, cancellation.Token);

        result.ShouldNotBeNull();
        result.Content.ShouldBe(content);
        result.ContentType.ShouldBe(contentType);
        result.Version.ShouldBe("7e18fa219fa44ad7a00e81334210d260678710c2fbac17cea23e8a64d22fe09b");
        storage.OpenedKeys.ShouldBe(["customer-logos/upload.bin"]);
        storage.ReceivedToken.ShouldBe(cancellation.Token);
        file.CopyToken.ShouldBe(cancellation.Token);
        file.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Non_raster_content_is_not_returned_even_with_an_image_extension()
    {
        await using var db = NewDb();
        var customer = CustomerWithLogo("customer-logos/upload.png");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var file = new TrackedStream("<svg></svg>"u8.ToArray());

        (await new CustomerLogoReader(db, new ReadOnlyStorage(file))
            .GetLogoAsync(customer.Id, CancellationToken.None)).ShouldBeNull();

        file.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancelled_copy_propagates_cancellation_and_disposes_the_file()
    {
        await using var db = NewDb();
        var customer = CustomerWithLogo("customer-logos/upload.png");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var file = new TrackedStream(new byte[12]);
        var storage = new ReadOnlyStorage(file, cancellation.Cancel);
        var reader = new CustomerLogoReader(db, storage);

        await Should.ThrowAsync<OperationCanceledException>(() => reader.GetLogoAsync(customer.Id, cancellation.Token));

        file.CopyToken.ShouldBe(cancellation.Token);
        file.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Versions_cover_only_requested_existing_customers_without_opening_files()
    {
        await using var db = NewDb();
        var withLogo = CustomerWithLogo("customer-logos/upload.bin");
        withLogo.Deactivate(DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        var withoutLogo = CustomerWithLogo(null);
        var unrequested = CustomerWithLogo("customer-logos/another.png");
        db.Customers.AddRange(withLogo, withoutLogo, unrequested);
        await db.SaveChangesAsync();
        var unknown = Guid.NewGuid();
        var storage = new ReadOnlyStorage(null);

        var versions = await new CustomerLogoReader(db, storage).GetLogoVersionsAsync(
            [withLogo.Id, withoutLogo.Id, unknown, withLogo.Id], CancellationToken.None);

        versions.Count.ShouldBe(2);
        versions[withLogo.Id].ShouldBe("7e18fa219fa44ad7a00e81334210d260678710c2fbac17cea23e8a64d22fe09b");
        versions[withoutLogo.Id].ShouldBe(CustomerLogoVersions.None);
        versions.ContainsKey(unknown).ShouldBeFalse();
        versions.ContainsKey(unrequested.Id).ShouldBeFalse();
        storage.OpenedKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_the_logo_reference_changes_its_metadata_to_none()
    {
        await using var db = NewDb();
        var customer = CustomerWithLogo("customer-logos/upload.png");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var storage = new ReadOnlyStorage(null);
        var reader = new CustomerLogoReader(db, storage);
        var before = await reader.GetLogoVersionsAsync([customer.Id], CancellationToken.None);
        before[customer.Id].ShouldNotBe("none");

        customer.SetLogo(null, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        var after = await reader.GetLogoVersionsAsync([customer.Id], CancellationToken.None);

        after[customer.Id].ShouldBe("none");
        storage.OpenedKeys.ShouldBeEmpty();
    }

    private static Customer CustomerWithLogo(string? reference) => Customer.Create(null, null, "Customer", Guid.NewGuid(),
        null, null, reference, Address.Create(null, null, null, null, null).Value, DateTimeOffset.UtcNow).Value;

    private static MasterDataDbContext NewDb() => new(new DbContextOptionsBuilder<MasterDataDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class ReadOnlyStorage(TrackedStream? stream, Action? onOpen = null) : IFileStorage
    {
        public List<string> OpenedKeys { get; } = [];
        public CancellationToken ReceivedToken { get; private set; }

        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            OpenedKeys.Add(storageKey);
            ReceivedToken = cancellationToken;
            onOpen?.Invoke();
            return Task.FromResult<Stream?>(stream);
        }

        public Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TrackedStream(byte[] content) : MemoryStream(content)
    {
        public bool Disposed { get; private set; }
        public CancellationToken CopyToken { get; private set; }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            CopyToken = cancellationToken;
            return base.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }
}
