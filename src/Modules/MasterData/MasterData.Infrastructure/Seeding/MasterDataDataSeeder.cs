using MasterData.Contracts.Seeding;
using MasterData.Domain.Countries;
using MasterData.Domain.Customers;
using MasterData.Domain.OperationTypes;
using MasterData.Domain.Services;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MasterData.Infrastructure.Seeding;

/// <summary>
/// Idempotent MasterData seeding for protected system records and the ISO 3166-1 country baseline.
/// Existing records are left untouched so startup never overwrites administrator edits.
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

        await SeedUnknownCustomerAsync(now, cancellationToken);
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

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded baseline MasterData operation and service catalogs.");
        }
    }

    private async Task SeedUnknownCustomerAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Customers.AnyAsync(c => c.Id == WellKnownMasterDataIds.UnknownCustomer, cancellationToken))
            return;

        var saudiArabiaId = await db.Countries
            .Where(c => c.IsoCode == "SA")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (saudiArabiaId is null)
        {
            logger.LogWarning("Skipped seeding Unknown Customer because the SA country is missing.");
            return;
        }

        var address = Address.Create(null, null, null, null, null);
        if (address.IsFailure)
        {
            logger.LogWarning("Skipped seeding Unknown Customer address: {Error}", address.Error.Description);
            return;
        }

        var result = Customer.Create(
            iataCode: null,
            icaoCode: null,
            name: "Unknown Customer",
            countryId: saudiArabiaId.Value,
            officialEmail: null,
            officialPhone: null,
            logoFileReference: null,
            address.Value,
            now,
            WellKnownMasterDataIds.UnknownCustomer);

        if (result.IsFailure)
        {
            logger.LogWarning("Skipped seeding Unknown Customer: {Error}", result.Error.Description);
            return;
        }

        db.Customers.Add(result.Value);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded Unknown Customer.");
    }
}
