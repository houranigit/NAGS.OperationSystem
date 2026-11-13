using System.Net;
using System.Text;
using BuildingBlocks.Application.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Operations.Application.Contracts;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Operations_batch_storage_failure_after_first_staged_file_keeps_old_work_and_outbox_and_cleans_staging()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8710", [actor.StaffId]);
        await SeedWorkArrivalAsync(admin, client, id);
        var recordId = Guid.NewGuid();
        var originalBytes = Encoding.ASCII.GetBytes("%PDF-original retained service document");
        var originalAttachment = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Document,
            "original.pdf", "application/pdf", originalBytes);
        var original = ServiceInput(refs.ServiceId, actor.StaffId) with { Attachments = [originalAttachment] };
        var initial = await OperationsBatchDetailAsync(client, id);
        using (var seed = await SendOperationsBatchAsync(client, id, OperationsBatchInput(services: [new(recordId, original)]), initial.RowVersion))
            seed.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var persisted = await db.Flights.AsNoTracking().Include(flight => flight.ServiceRecords)
            .ThenInclude(service => service.Attachments).SingleAsync(flight => flight.Id == id);
        var oldKey = persisted.Services.Single().Attachments.Single().Details.StorageKey;
        var innerStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var failingStorage = new FailSecondBatchFileStorage(innerStorage);
        using var faultHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IFileStorage>(failingStorage))));
        using var faultClient = faultHost.CreateClient();
        faultClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var before = await OperationsBatchSnapshotAsync(client, id);
        var replacement = original with
        {
            Notes = "This correction must not publish",
            Attachments = [new(Guid.NewGuid(), (int)AttachmentKind.Document, "replacement.pdf", "application/pdf",
                Encoding.ASCII.GetBytes("%PDF-new valid service document"))]
        };
        var task = TaskInput(actor.StaffId) with
        {
            Attachments = [new(Guid.NewGuid(), (int)AttachmentKind.Image, "task.png", "image/png", ServicePng())]
        };
        failingStorage.Arm();

        using var rejected = await SendOperationsBatchAsync(faultClient, id,
            OperationsBatchInput(services: [new(recordId, replacement)], tasks: [new(Guid.NewGuid(), task)]), before.Detail.RowVersion);

        rejected.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        failingStorage.SaveCalls.ShouldBe(2);
        var staged = failingStorage.StoredKeys.ShouldHaveSingleItem();
        await AssertOperationsBatchUnchangedAsync(client, id, before);
        await using var orphan = await innerStorage.OpenAsync(staged);
        orphan.ShouldBeNull();
        await using var oldFile = await innerStorage.OpenAsync(oldKey);
        oldFile.ShouldNotBeNull();
        using var buffer = new MemoryStream();
        await oldFile.CopyToAsync(buffer);
        buffer.ToArray().ShouldBe(originalBytes);
    }

    private sealed class FailSecondBatchFileStorage(IFileStorage inner) : IFileStorage
    {
        private bool _armed;
        public int SaveCalls { get; private set; }
        public List<string> StoredKeys { get; } = [];

        public void Arm()
        {
            SaveCalls = 0;
            StoredKeys.Clear();
            _armed = true;
        }

        public async Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content,
            CancellationToken cancellationToken = default)
        {
            if (_armed && ++SaveCalls == 2)
                throw new IOException("Simulated failure while staging the second batch attachment.");
            var saved = await inner.SaveAsync(container, fileName, contentType, content, cancellationToken);
            if (_armed) StoredKeys.Add(saved.StorageKey);
            return saved;
        }

        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) =>
            inner.OpenAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(storageKey, cancellationToken);
    }
}
