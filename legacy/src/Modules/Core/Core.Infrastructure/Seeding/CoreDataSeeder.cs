using Core.Contracts.Seeding;
using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Aggregates.Service;
using Core.Domain.ValueObjects;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Core.Infrastructure.Seeding;

public static class CoreDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CoreDbContext>>();

        try
        {
            await SeedCurrenciesAsync(db, logger);
            await SeedCountriesAsync(db, logger);
            await SeedOperationTypesAsync(db, logger);
            await SeedServicesAsync(db, logger);

            logger.LogInformation("Core reference data seeding completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Core reference data seeding failed.");
        }
    }

    private static async Task SeedCurrenciesAsync(CoreDbContext db, ILogger logger)
    {
        var sarId = CurrencyId.From(CoreSeedIds.SarCurrency);
        var usdId = CurrencyId.From(CoreSeedIds.UsdCurrency);

        if (!await db.Currencies.AnyAsync(c => c.Id == sarId))
        {
            var sarCode = CurrencyCode.Create("SAR").Value!;
            db.Currencies.Add(Currency.CreateSeed(CoreSeedIds.SarCurrency, sarCode, "Saudi Riyal"));
            logger.LogInformation("Seeding currency: SAR");
        }

        if (!await db.Currencies.AnyAsync(c => c.Id == usdId))
        {
            var usdCode = CurrencyCode.Create("USD").Value!;
            db.Currencies.Add(Currency.CreateSeed(CoreSeedIds.UsdCurrency, usdCode, "US Dollar"));
            logger.LogInformation("Seeding currency: USD");
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();

        var sar = await db.Currencies
            .Include(c => c.ExchangeRates)
            .FirstOrDefaultAsync(c => c.Id == sarId);

        var usd = await db.Currencies
            .Include(c => c.ExchangeRates)
            .FirstOrDefaultAsync(c => c.Id == usdId);

        if (sar is null || usd is null)
            return;

        bool rateAdded = false;

        if (!sar.ExchangeRates.Any(r => r.ToCurrencyId == usdId))
        {
            _ = sar.AddExchangeRate(usdId, 0.2667m, CoreSeedIds.SystemUserId);
            logger.LogInformation("Seeding exchange rate: SAR → USD (0.2667)");
            rateAdded = true;
        }

        if (!usd.ExchangeRates.Any(r => r.ToCurrencyId == sarId))
        {
            _ = usd.AddExchangeRate(sarId, 3.75m, CoreSeedIds.SystemUserId);
            logger.LogInformation("Seeding exchange rate: USD → SAR (3.75)");
            rateAdded = true;
        }

        if (rateAdded)
            await db.SaveChangesAsync();
    }

    private static async Task SeedCountriesAsync(CoreDbContext db, ILogger logger)
    {
        var saId = CountryId.From(CoreSeedIds.SaudiArabia);

        if (!await db.Countries.AnyAsync(c => c.Id == saId))
        {
            var saCode = CountryCode.Create("SA").Value!;
            db.Countries.Add(Country.CreateSeed(CoreSeedIds.SaudiArabia, saCode, "Saudi Arabia"));
            logger.LogInformation("Seeding country: SA — Saudi Arabia");
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedOperationTypesAsync(CoreDbContext db, ILogger logger)
    {
        var adHocId = OperationTypeId.From(CoreSeedIds.AdHocOperationType);

        if (!await db.OperationTypes.AnyAsync(o => o.Id == adHocId))
        {
            db.OperationTypes.Add(OperationType.CreateSeed(
                CoreSeedIds.AdHocOperationType,
                "Ad Hoc",
                "Unscheduled, on-demand operations not tied to a regular programme."));

            logger.LogInformation("Seeding system operation type: Ad Hoc");
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedServicesAsync(CoreDbContext db, ILogger logger)
    {
        var aogId = ServiceId.From(CoreSeedIds.AogService);
        var onCallId = ServiceId.From(CoreSeedIds.OnCallService);

        bool changed = false;

        if (!await db.Services.AnyAsync(s => s.Id == aogId))
        {
            db.Services.Add(Service.CreateSeed(
                CoreSeedIds.AogService,
                "AOG (Aircraft Per Landing)",
                "Aircraft on ground — emergency technical service billed per aircraft landing."));

            logger.LogInformation("Seeding system service: AOG");
            changed = true;
        }

        if (!await db.Services.AnyAsync(s => s.Id == onCallId))
        {
            db.Services.Add(Service.CreateSeed(
                CoreSeedIds.OnCallService,
                "On Call",
                "On-call standby technical support — billed per hour."));

            logger.LogInformation("Seeding system service: ONCALL — On Call");
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync();
    }
}
