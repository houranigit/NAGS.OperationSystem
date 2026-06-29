using MasterData.Contracts.Seeding;
using MasterData.Domain.Countries;
using MasterData.Domain.OperationTypes;
using MasterData.Domain.Services;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MasterData.Infrastructure.Seeding;

/// <summary>
/// Idempotent MasterData seeding. Inserts missing ISO 3166-1 baseline countries with stable ids and
/// never overwrites administrator edits: a country already present (by code) is left untouched.
/// </summary>
public sealed class MasterDataDataSeeder(
    MasterDataDbContext db,
    TimeProvider timeProvider,
    ILogger<MasterDataDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await SeedCatalogsAsync(now, cancellationToken);

        var existingCodes = await db.Countries
            .Select(c => c.IsoCode)
            .ToListAsync(cancellationToken);
        var existing = existingCodes.ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var (code, name) in CountrySeedData.All)
        {
            if (existing.Contains(code))
                continue;

            var result = Country.Create(name, code, now, MasterDataSeedIds.For("country", code));
            if (result.IsFailure)
            {
                logger.LogWarning("Skipped seeding country {Code}: {Error}", code, result.Error.Description);
                continue;
            }

            db.Countries.Add(result.Value);
            existing.Add(code);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} baseline countries.", added);
        }
    }

    private async Task SeedCatalogsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = false;

        if (!await db.OperationTypes.AnyAsync(o => o.Id == WellKnownMasterDataIds.AdHocOperationType, cancellationToken))
        {
            var result = OperationType.Create(
                "Ad Hoc",
                "Unscheduled, on-demand operations not tied to a regular programme.",
                now,
                WellKnownMasterDataIds.AdHocOperationType);

            if (result.IsSuccess)
            {
                db.OperationTypes.Add(result.Value);
                changed = true;
            }
            else
            {
                logger.LogWarning("Skipped seeding Ad Hoc operation type: {Error}", result.Error.Description);
            }
        }

        if (!await db.Services.AnyAsync(s => s.Id == WellKnownMasterDataIds.AircraftPerLandingService, cancellationToken))
        {
            var result = Service.Create(
                "Aircraft Per Landing",
                "Aircraft service billed per landing.",
                now,
                WellKnownMasterDataIds.AircraftPerLandingService);

            if (result.IsSuccess)
            {
                db.Services.Add(result.Value);
                changed = true;
            }
            else
            {
                logger.LogWarning("Skipped seeding Aircraft Per Landing service: {Error}", result.Error.Description);
            }
        }

        if (!await db.Services.AnyAsync(s => s.Id == WellKnownMasterDataIds.OnCallService, cancellationToken))
        {
            var result = Service.Create(
                "On Call",
                "On-call standby technical support billed per hour.",
                now,
                WellKnownMasterDataIds.OnCallService);

            if (result.IsSuccess)
            {
                db.Services.Add(result.Value);
                changed = true;
            }
            else
            {
                logger.LogWarning("Skipped seeding On Call service: {Error}", result.Error.Description);
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded baseline MasterData operation and service catalogs.");
        }
    }
}
