using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Authorization;
using MasterData.Application.Features.Customers;
using MasterData.Contracts.Readers;
using MasterData.Domain.Customers;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace MasterData.Application.UnitTests;

public sealed class CustomerLogoVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private const string OriginalKey = "customer-logos/original.png";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Missing_reference_has_the_explicit_none_version(string? reference) =>
        CustomerLogoVersions.FromReference(reference).ShouldBe("none");

    [Fact]
    public async Task Options_publish_opaque_logo_versions_that_are_stable_across_customer_name_changes()
    {
        await using var db = NewDb();
        var customer = NewCustomer(OriginalKey);
        var withoutLogo = NewCustomer(null);
        db.Customers.AddRange(customer, withoutLogo);
        await db.SaveChangesAsync();
        var query = new GetActiveCustomerOptionsQueryHandler(db, AdministratorScope());
        var before = await query.Handle(new GetActiveCustomerOptionsQuery(), CancellationToken.None);
        var version = before.Value.Single(option => option.Id == customer.Id).LogoVersion;

        version.ShouldBe("d484ee6bfca0e9c376d45f7b21e1ccd746b648b7a3bacc323523f7e090bfdb2f");
        before.Value.Single(option => option.Id == withoutLogo.Id).LogoVersion.ShouldBe("none");
        customer.Update(customer.IataCode, customer.IcaoCode, "Renamed customer", customer.CountryId,
            customer.OfficialEmail, customer.OfficialPhone, customer.Address, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        var after = await query.Handle(new GetActiveCustomerOptionsQuery(), CancellationToken.None);

        after.Value.Single(option => option.Id == customer.Id).Name.ShouldBe("Renamed customer");
        after.Value.Single(option => option.Id == customer.Id).LogoVersion.ShouldBe(version);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Successful_logo_change_updates_the_version_and_enqueues_only_a_customers_refresh(bool replaceExisting, bool inactive)
    {
        await using var db = NewDb();
        var customer = await SeedCustomer(db, replaceExisting ? OriginalKey : null);
        if (inactive)
        {
            customer.Deactivate(Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }
        var storage = new LogoStorage();
        if (replaceExisting) storage.Files[OriginalKey] = Png;
        var sync = new RecordingSync();
        byte[] replacement = [.. Png, 1];

        var result = await Handler(db, storage, sync).Handle(
            new SetCustomerLogoCommand(customer.Id, replacement, "new.png", "image/png", [1]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var key = result.Value;
        key.ShouldNotBe(OriginalKey);
        db.ChangeTracker.Clear();
        (await db.Customers.SingleAsync(item => item.Id == customer.Id)).LogoFileReference.ShouldBe(key);
        CustomerLogoVersions.FromReference(key).ShouldNotBe(CustomerLogoVersions.FromReference(replaceExisting ? OriginalKey : null));
        storage.Files[key].ShouldBe(replacement);
        storage.SavedKeys.ShouldBe([key]);
        storage.DeletedKeys.ShouldBe(replaceExisting ? new[] { OriginalKey } : Array.Empty<string>());
        var hint = sync.Changes.ShouldHaveSingleItem();
        hint.Table.ShouldBe(MobileSyncTables.Customers);
        hint.Op.ShouldBe(MobileSyncOps.Refresh);
    }

    [Fact]
    public async Task Identical_bytes_preserve_the_reference_version_and_timestamp_without_writes_or_hints()
    {
        await using var db = NewDb();
        var customer = await SeedCustomer(db, OriginalKey);
        var storage = new LogoStorage();
        storage.Files[OriginalKey] = Png;
        var sync = new RecordingSync();

        var result = await Handler(db, storage, sync).Handle(
            new SetCustomerLogoCommand(customer.Id, Png, "renamed.png", "image/png", [1]), CancellationToken.None);

        result.Value.ShouldBe(OriginalKey);
        customer.LogoFileReference.ShouldBe(OriginalKey);
        customer.UpdatedAtUtc.ShouldBeNull();
        CustomerLogoVersions.FromReference(result.Value).ShouldBe(CustomerLogoVersions.FromReference(OriginalKey));
        db.ChangeTracker.HasChanges().ShouldBeFalse();
        storage.SavedKeys.ShouldBeEmpty();
        storage.DeletedKeys.ShouldBeEmpty();
        sync.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Identical_upload_with_a_stale_rowversion_is_rejected_without_hints()
    {
        await using var db = NewDb();
        var customer = await SeedCustomer(db, OriginalKey);
        var storage = new LogoStorage();
        storage.Files[OriginalKey] = Png;
        var sync = new RecordingSync();

        var result = await Handler(db, storage, sync).Handle(
            new SetCustomerLogoCommand(customer.Id, Png, "same.png", "image/png", [2]), CancellationToken.None);

        result.Error.Code.ShouldBe("General.ConcurrencyConflict");
        customer.LogoFileReference.ShouldBe(OriginalKey);
        storage.SavedKeys.ShouldBeEmpty();
        storage.DeletedKeys.ShouldBeEmpty();
        sync.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_content_does_not_replace_the_logo_or_enqueue_a_hint()
    {
        await using var db = NewDb();
        var customer = await SeedCustomer(db, OriginalKey);
        var storage = new LogoStorage();
        storage.Files[OriginalKey] = Png;
        var sync = new RecordingSync();

        var result = await Handler(db, storage, sync).Handle(
            new SetCustomerLogoCommand(customer.Id, new byte[12], "bad.png", "image/png", [1]), CancellationToken.None);

        result.Error.Code.ShouldBe("MasterData.Customer.LogoInvalidType");
        customer.LogoFileReference.ShouldBe(OriginalKey);
        storage.SavedKeys.ShouldBeEmpty();
        storage.DeletedKeys.ShouldBeEmpty();
        sync.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failed_persistence_removes_the_new_file_and_does_not_enqueue_a_hint()
    {
        var reject = new RejectSaveInterceptor();
        await using var db = NewDb(reject);
        var customer = await SeedCustomer(db, OriginalKey);
        var storage = new LogoStorage();
        storage.Files[OriginalKey] = Png;
        var sync = new RecordingSync();
        reject.Reject = true;

        var result = await Handler(db, storage, sync).Handle(
            new SetCustomerLogoCommand(customer.Id, [.. Png, 1], "new.png", "image/png", [1]), CancellationToken.None);

        result.Error.Code.ShouldBe("General.ConcurrencyConflict");
        storage.DeletedKeys.ShouldBe(storage.SavedKeys);
        storage.Files.Keys.ShouldBe([OriginalKey]);
        sync.Changes.ShouldBeEmpty();
        db.ChangeTracker.Clear();
        (await db.Customers.SingleAsync(item => item.Id == customer.Id)).LogoFileReference.ShouldBe(OriginalKey);
    }

    private static SetCustomerLogoCommandHandler Handler(MasterDataDbContext db, LogoStorage storage, RecordingSync sync) =>
        new(db, AdministratorScope(), storage, TimeProvider.System, sync);

    private static IMasterDataScope AdministratorScope() => new StaticScope(new(UserType.SystemAdministrator, null, null));

    private static Customer NewCustomer(string? reference) => Customer.Create(null, null, "Customer", Guid.NewGuid(),
        null, null, reference, Address.Create(null, null, null, null, null).Value, Now).Value;

    private static async Task<Customer> SeedCustomer(MasterDataDbContext db, string? reference)
    {
        var customer = NewCustomer(reference);
        db.Customers.Add(customer);
        db.Entry(customer).Property(item => item.RowVersion).CurrentValue = [1];
        await db.SaveChangesAsync();
        return customer;
    }

    private static MasterDataDbContext NewDb(RejectSaveInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString());
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new MasterDataDbContext(options.Options);
    }

    private sealed class StaticScope(MasterDataScopeContext context) : IMasterDataScope
    {
        public Task<Result<MasterDataScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class RecordingSync : IMobileSyncBroadcaster
    {
        public List<MobileSyncChange> Changes { get; } = [];
        public void Enqueue(MobileSyncChange change) => Changes.Add(change);
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RejectSaveInterceptor : SaveChangesInterceptor
    {
        public bool Reject { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Reject ? throw new DbUpdateConcurrencyException("Concurrent update") : ValueTask.FromResult(result);
    }

    private sealed class LogoStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<string> SavedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];

        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storageKey, out var content) ? new MemoryStream(content) : null);

        public async Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content,
            CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var key = $"{container}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
            Files[key] = memory.ToArray();
            SavedKeys.Add(key);
            return new StoredFile(key, contentType, memory.Length);
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Files.Remove(storageKey);
            DeletedKeys.Add(storageKey);
            return Task.CompletedTask;
        }
    }
}
