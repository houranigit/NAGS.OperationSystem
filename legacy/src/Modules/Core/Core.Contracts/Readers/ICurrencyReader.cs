using Core.Contracts.Features.Currency;

namespace Core.Contracts.Readers;

public interface ICurrencyReader
{
    Task<CurrencySnapshot?> GetByIdAsync(Guid currencyId, CancellationToken cancellationToken = default);

    /// <summary>True when a currency with this id exists AND is active.</summary>
    Task<bool> ExistsActiveAsync(Guid currencyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when an exchange rate from <paramref name="fromCurrencyId"/> to
    /// <paramref name="toCurrencyId"/> is set on or before <paramref name="onDate"/>. Used by
    /// the Contracts module to enforce that the chosen contract currency can be converted to
    /// the platform currency for billing.
    /// </summary>
    Task<bool> HasRateToAsync(
        Guid fromCurrencyId,
        Guid toCurrencyId,
        DateTime onDate,
        CancellationToken cancellationToken = default);
}
