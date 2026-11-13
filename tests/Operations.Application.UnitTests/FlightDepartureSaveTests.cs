using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightDepartureSaveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");

    [Fact]
    public async Task Unexpected_save_failure_cleans_only_unreferenced_new_bytes_and_preserves_the_original_error()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failure = new InvalidOperationException("Injected save failure");
        fixture.Writes.Save = _ => throw failure;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => fixture.SaveAsync());

        thrown.ShouldBeSameAs(failure);
        fixture.Storage.Files.ShouldBeEmpty();
        fixture.Storage.DeleteTokens.ShouldHaveSingleItem().CanBeCanceled.ShouldBeFalse();
    }

    [Fact]
    public async Task Activity_read_failure_after_signature_staging_cleans_unreferenced_bytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Writes.ThrowOnActivityRead = true;
        await Should.ThrowAsync<IOException>(() => fixture.SaveAsync());
        fixture.Storage.Files.ShouldBeEmpty();
        fixture.Storage.DeleteTokens.ShouldHaveSingleItem().CanBeCanceled.ShouldBeFalse();
        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.Flights.Include(item => item.DepartureSubmissions).SingleAsync()).DepartureSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Canceled_save_does_not_cancel_cleanup_of_the_unreferenced_signature()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var canceled = new CancellationTokenSource();
        fixture.Writes.Save = _ => { canceled.Cancel(); throw new OperationCanceledException(canceled.Token); };

        await Should.ThrowAsync<OperationCanceledException>(() => fixture.SaveAsync(canceled.Token));

        fixture.Storage.Files.ShouldBeEmpty();
        fixture.Storage.DeleteTokens.ShouldHaveSingleItem().CanBeCanceled.ShouldBeFalse();
    }

    [Fact]
    public async Task Ambiguous_commit_keeps_referenced_bytes_and_an_exact_retry_acknowledges_the_saved_departure()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Writes.Save = async _ =>
        {
            await fixture.Db.SaveChangesAsync(CancellationToken.None);
            throw new InvalidOperationException("Injected response loss after commit");
        };
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.SaveAsync());
        fixture.Storage.Files.ShouldHaveSingleItem();
        fixture.Storage.DeleteTokens.ShouldBeEmpty();
        fixture.Db.ChangeTracker.Clear();

        var replay = await fixture.SaveAsync();

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.Submission.HandledByEmployee.StaffMemberId.ShouldBe(fixture.StaffId);
        fixture.Storage.Files.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Concurrency_rejection_releases_its_unreferenced_signature_and_returns_a_reviewable_conflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Writes.Save = _ => throw new DbUpdateConcurrencyException("Injected race");

        var result = await fixture.SaveAsync();

        result.Error.Code.ShouldBe("General.ConcurrencyConflict");
        fixture.Storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cleanup_failure_does_not_mask_the_original_database_failure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failure = new InvalidOperationException("Injected save failure");
        fixture.Writes.Save = _ => throw failure;
        fixture.Storage.ThrowOnDelete = true;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => fixture.SaveAsync());

        thrown.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Previous_file_cleanup_failure_does_not_turn_a_committed_correction_into_a_failed_save()
    {
        await using var fixture = await Fixture.CreateAsync();
        var saved = await fixture.SaveAsync();
        saved.IsSuccess.ShouldBeTrue();
        fixture.Storage.ThrowOnDelete = true;

        var corrected = await fixture.Handler.Handle(new SaveFlightDepartureCommand(fixture.Flight.Id,
            fixture.Input with { SignatureSignedAtUtc = Now.AddSeconds(1) }, fixture.Flight.RowVersion), CancellationToken.None);

        corrected.IsSuccess.ShouldBeTrue();
        corrected.Value.Submission.Id.ShouldBe(saved.Value.Submission.Id);
        fixture.Storage.Files.Count.ShouldBe(2); // Old-file cleanup is best effort; accepted bytes stay intact.
    }

    [Fact]
    public async Task Unsigned_departure_roundtrips_and_omission_retains_a_later_signature()
    {
        await using var f = await Fixture.CreateAsync();
        var unsigned = f.Input with { SignatureContent = null, SignatureSignedAtUtc = null };
        var saved = await f.Handler.Handle(new(f.Flight.Id, unsigned, f.Flight.RowVersion), CancellationToken.None);
        saved.IsSuccess.ShouldBeTrue(); saved.Value.Submission.HasSignature.ShouldBeFalse();
        saved.Value.Submission.SignatureVersion.ShouldBeNull(); f.Storage.Files.ShouldBeEmpty();
        var signed = await f.SaveAsync(); signed.IsSuccess.ShouldBeTrue();
        signed.Value.Submission.HasSignature.ShouldBeTrue();
        var corrected = await f.Handler.Handle(new(f.Flight.Id, unsigned with { Remarks = "Updated" }, f.Flight.RowVersion), CancellationToken.None);
        corrected.IsSuccess.ShouldBeTrue(); corrected.Value.Submission.SignatureVersion.ShouldBe(signed.Value.Submission.SignatureVersion);
        corrected.Value.Submission.HasSignature.ShouldBeTrue(); f.Storage.Files.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Departure_requires_arrival_before_signature_upload()
    {
        await using var f = await Fixture.CreateAsync(withArrival: false);
        (await f.SaveAsync()).Error.Code.ShouldBe("Operations.Flight.ArrivalRequired");
        f.Storage.Files.ShouldBeEmpty(); f.Flight.DepartureSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Departure_before_arrival_is_rejected_and_equal_actuals_are_allowed()
    {
        await using var f = await Fixture.CreateAsync();
        var early = f.Input with { Atd = Now.AddHours(-2) };
        (await f.Handler.Handle(new(f.Flight.Id, early, f.Flight.RowVersion), CancellationToken.None))
            .Error.Code.ShouldBe("Operations.Flight.DepartureBeforeArrival");
        var equal = f.Input with { Atd = Now.AddHours(-1), SignatureContent = null, SignatureSignedAtUtc = null };
        (await f.Handler.Handle(new(f.Flight.Id, equal, f.Flight.RowVersion), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required OperationsDbContext Db { get; init; }
        public required WriteBoundary Writes { get; init; }
        public required MemoryStorage Storage { get; init; }
        public required Flight Flight { get; init; }
        public required Guid StaffId { get; init; }
        public required SaveFlightDepartureCommandHandler Handler { get; init; }
        public SaveFlightDepartureRequest Input { get; } = new(Now, Png, Now.AddMinutes(-1), "Departure");

        public Task<Result<FlightDepartureSavedDto>> SaveAsync(CancellationToken ct = default) =>
            Handler.Handle(new SaveFlightDepartureCommand(Flight.Id, Input, Flight.RowVersion), ct);

        public static async Task<Fixture> CreateAsync(bool withArrival = true)
        {
            var db = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var reader = new SchedulingMasterDataReader();
            var staffId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            reader.StaffMembers[staffId] = new(staffId, "Handling employee", "E1", reader.StationId, userId, true);
            var flight = Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Customer"),
                new StationSnapshot(reader.StationId, "RUH", "Riyadh"),
                new OperationTypeSnapshot(Guid.NewGuid(), "Transit"), FlightNumber.Create("SV100", "SV").Value,
                ScheduledTime.Create(Now.AddHours(-1), Now.AddHours(1)).Value, null,
                [new ServiceSnapshot(reader.ServiceId, "Service")], [new StaffMemberSnapshot(staffId, "Handling employee", "E1")], userId, Now).Value;
            if (withArrival)
            {
                var employee = new StaffMemberSnapshot(staffId, "Handling employee", "E1");
                flight.SubmitArrival(employee, ArrivalDetails.Create(null, Now.AddHours(-1),
                    new AircraftTypeSnapshot(Guid.NewGuid(), "Boeing", "737"), "HZ-TEST", employee, null).Value,
                    userId, Now.AddHours(-1)).IsSuccess.ShouldBeTrue();
            }
            db.Flights.Add(flight);
            await db.SaveChangesAsync();
            var writes = new WriteBoundary(db);
            var storage = new MemoryStorage();
            return new Fixture { Db = db, Writes = writes, Storage = storage, Flight = flight, StaffId = staffId,
                Handler = new SaveFlightDepartureCommandHandler(writes,
                    new Scope(new(reader.StationId, staffId, userId, Now)), new MasterDataResolver(reader),
                    new Clock(), NoopMobileSyncBroadcaster.Instance, storage) };
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class WriteBoundary(OperationsDbContext db) : IOperationsDbContext
    {
        public Func<CancellationToken, Task<int>>? Save { get; set; }
        public bool ThrowOnActivityRead { get; set; }
        public DbSet<Flight> Flights => db.Flights;
        public DbSet<FlightUserActivity> FlightUserActivities => ThrowOnActivityRead ? throw new IOException("Injected activity read failure") : db.FlightUserActivities;
        public DbSet<OutboxMessage> OutboxMessages => db.OutboxMessages;
        public DbSet<InboxMessage> InboxMessages => db.InboxMessages;
        public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
            db.SetOriginalRowVersion(entity, rowVersion);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Save?.Invoke(cancellationToken) ?? db.SaveChangesAsync(cancellationToken);
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<CancellationToken> DeleteTokens { get; } = [];
        public bool ThrowOnDelete { get; set; }
        public async Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var key = $"{container}/{Guid.NewGuid():N}.png";
            Files[key] = buffer.ToArray();
            return new(key, contentType, buffer.Length);
        }
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeleteTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnDelete) throw new IOException("Injected cleanup failure");
            Files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class Scope(MobileFlightScopeContext context) : IMobileFlightScope
    {
        public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
