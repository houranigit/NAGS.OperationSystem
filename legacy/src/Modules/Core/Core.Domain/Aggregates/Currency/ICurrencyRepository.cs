namespace Core.Domain.Aggregates.Currency;

public interface ICurrencyRepository
{
    Task<Currency?> GetByIdAsync(CurrencyId id, CancellationToken ct = default);
    Task<Currency?> GetByIdWithRatesAsync(CurrencyId id, CancellationToken ct = default);
    Task<Currency?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Currency>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Currency>> GetAllActiveAsync(CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Returns the most recent rate set on or before today.</summary>
    Task<ExchangeRate?> GetCurrentRateAsync(CurrencyId fromId, CurrencyId toId, CancellationToken ct = default);

    /// <summary>Returns the most recent rate set on or before the given date (point-in-time billing).</summary>
    Task<ExchangeRate?> GetRateOnDateAsync(CurrencyId fromId, CurrencyId toId, DateTime date, CancellationToken ct = default);

    Task<IReadOnlyList<ExchangeRate>> GetRateHistoryAsync(CurrencyId fromId, CurrencyId toId, CancellationToken ct = default);

    void Add(Currency currency);
    void Update(Currency currency);
}
