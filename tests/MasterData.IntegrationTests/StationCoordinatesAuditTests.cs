using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Auditing;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Infrastructure.Auditing;
using MasterData.Domain.Stations;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MasterData.IntegrationTests;

public sealed class StationCoordinatesAuditTests(MasterDataApiFactory factory)
    : IClassFixture<MasterDataApiFactory>
{
    [Fact]
    public async Task Complex_fields_redact_sensitive_parents_and_leaves_before_capture()
    {
        var options = new DbContextOptionsBuilder<AuditProbeContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true")
            .AddInterceptors(new AuditSaveChangesInterceptor(new SystemAuditContext()), new CaptureOnlySaveInterceptor())
            .Options;
        await using var db = new AuditProbeContext(options);
        db.Add(new AuditProbe
        {
            Id = Guid.NewGuid(),
            Details = new AuditDetails { Label = "Visible detail", ApiKey = "excluded leaf value" },
            Credentials = new AuditDetails { Label = "excluded parent value", ApiKey = "excluded nested value" }
        });

        await db.SaveChangesAsync();

        var envelope = db.OutboxMessages.Local.ShouldHaveSingleItem();
        var capture = JsonSerializer.Deserialize<AuditEntryRecorded>(envelope.Content)!;
        capture.Changes.ShouldContain(new AuditFieldChange("Details.Label", null, "Visible detail"));
        capture.Changes.ShouldNotContain(change => change.Field.StartsWith("Credentials", StringComparison.Ordinal));
        capture.Changes.ShouldNotContain(change => change.Field.EndsWith("ApiKey", StringComparison.Ordinal));
        envelope.Content.ShouldNotContain("excluded");
    }

    [Fact]
    public async Task Coordinate_creation_replacement_clearing_and_restoration_are_audited_as_scalar_changes()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
        var countryId = await db.Countries.Where(country => country.IsoCode == "SA").Select(country => country.Id).SingleAsync();
        var now = new DateTimeOffset(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
        var coordinates = GeoCoordinates.Create(24.9576m, 46.6988m).Value;
        var station = Station.Create("AUD", null, "Audit station", "Riyadh", countryId, now,
            geoCoordinates: coordinates, timeZoneId: "Asia/Riyadh").Value;
        var capturedIds = new HashSet<Guid>();
        db.Stations.Add(station);

        await db.SaveChangesAsync();

        var created = await NewCaptureAsync();
        created.Action.ShouldBe(AuditActions.Created);
        created.Changes.ShouldContain(change => change.Field == "Name" && change.Before == null && change.After == "Audit station");
        AssertCoordinate(created, "GeoCoordinates.Latitude", null, 24.9576m);
        AssertCoordinate(created, "GeoCoordinates.Longitude", null, 46.6988m);

        // Reload across saves so these assertions cover the relational snapshot, including nulls.
        db.ChangeTracker.Clear();
        station = await db.Stations.SingleAsync(item => item.Id == station.Id);
        station.Update(station.IataCode, station.IcaoCode, station.Name, station.City, countryId, now.AddMinutes(1),
            GeoCoordinates.Create(0m, 46.6988m).Value, station.TimeZoneId).IsSuccess.ShouldBeTrue();

        await db.SaveChangesAsync();

        var updated = await NewCaptureAsync();
        updated.Action.ShouldBe(AuditActions.Updated);
        AssertCoordinate(updated, "GeoCoordinates.Latitude", 24.9576m, 0m);
        updated.Changes.ShouldNotContain(change => change.Field == "GeoCoordinates.Longitude");
        updated.Changes.ShouldNotContain(change => change.Field == "Name");

        db.ChangeTracker.Clear();
        station = await db.Stations.SingleAsync(item => item.Id == station.Id);
        station.Update(station.IataCode, station.IcaoCode, station.Name, station.City, countryId, now.AddMinutes(2),
            geoCoordinates: null, timeZoneId: station.TimeZoneId).IsSuccess.ShouldBeTrue();

        await db.SaveChangesAsync();

        var cleared = await NewCaptureAsync();
        cleared.Action.ShouldBe(AuditActions.Updated);
        AssertCoordinate(cleared, "GeoCoordinates.Latitude", 0m, null);
        AssertCoordinate(cleared, "GeoCoordinates.Longitude", 46.6988m, null);

        db.ChangeTracker.Clear();
        station = await db.Stations.SingleAsync(item => item.Id == station.Id);
        station.GeoCoordinates.ShouldBeNull();
        station.Update(station.IataCode, station.IcaoCode, station.Name, station.City, countryId, now.AddMinutes(3),
            coordinates, station.TimeZoneId).IsSuccess.ShouldBeTrue();

        await db.SaveChangesAsync();

        var restored = await NewCaptureAsync();
        restored.Action.ShouldBe(AuditActions.Updated);
        AssertCoordinate(restored, "GeoCoordinates.Latitude", null, 24.9576m);
        AssertCoordinate(restored, "GeoCoordinates.Longitude", null, 46.6988m);

        station = Station.Create("AUE", null, "Station without coordinates", "Riyadh", countryId, now,
            timeZoneId: "Asia/Riyadh").Value;
        db.Stations.Add(station);
        await db.SaveChangesAsync();
        (await NewCaptureAsync()).Changes.ShouldNotContain(change => change.Field.StartsWith("GeoCoordinates.", StringComparison.Ordinal));

        async Task<AuditEntryRecorded> NewCaptureAsync()
        {
            var contents = await db.OutboxMessages.AsNoTracking()
                .Where(message => message.Type.Contains(nameof(AuditEntryRecorded)))
                .Select(message => message.Content).ToListAsync();
            var capture = contents.Select(content => JsonSerializer.Deserialize<AuditEntryRecorded>(content)!)
                .Where(entry => entry.EntityId == station.Id && !capturedIds.Contains(entry.EventId))
                .ShouldHaveSingleItem();
            capturedIds.Add(capture.EventId);
            return capture;
        }
    }
    private static void AssertCoordinate(AuditEntryRecorded entry, string field, decimal? expectedBefore, decimal? expectedAfter)
    {
        var change = entry.Changes.Where(change => change.Field == field).ShouldHaveSingleItem();
        if (expectedBefore is { } before)
            decimal.Parse(change.Before!, CultureInfo.InvariantCulture).ShouldBe(before);
        else
            change.Before.ShouldBe(entry.Action == AuditActions.Created ? null : string.Empty);

        if (expectedAfter is { } after)
            decimal.Parse(change.After!, CultureInfo.InvariantCulture).ShouldBe(after);
        else
            change.After.ShouldBe(string.Empty);
    }

    private sealed class AuditProbeContext(DbContextOptions<AuditProbeContext> options) : DbContext(options), IOutboxDbContext
    {
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditProbe>(builder =>
            {
                builder.HasKey(probe => probe.Id);
                builder.ComplexProperty(probe => probe.Details);
                builder.ComplexProperty(probe => probe.Credentials);
            });
            modelBuilder.Entity<OutboxMessage>().HasKey(message => message.Id);
            modelBuilder.Entity<InboxMessage>().HasKey(message => new { message.MessageId, message.Consumer });
        }
    }

    private sealed class AuditProbe : IAuditable
    {
        public Guid Id { get; init; }
        public AuditDetails Details { get; init; } = null!;
        public AuditDetails Credentials { get; init; } = null!;
        string IAuditable.AuditEntityType => "Probe";
        Guid IAuditable.AuditEntityId => Id;
    }

    private sealed class AuditDetails
    {
        public string Label { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;
    }

    // Exercise the real EF/interceptor pipeline while retaining the captured outbox in memory.
    private sealed class CaptureOnlySaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }

}
