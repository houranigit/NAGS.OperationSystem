using MasterData.Domain.Countries;
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
}
