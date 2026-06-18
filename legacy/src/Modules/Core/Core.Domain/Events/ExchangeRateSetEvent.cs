using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Currency;

namespace Core.Domain.Events;

public sealed class ExchangeRateSetEvent(
    ExchangeRateId exchangeRateId,
    CurrencyId fromCurrencyId,
    CurrencyId toCurrencyId,
    decimal rate,
    Guid createdById) : DomainEvent
{
    public ExchangeRateId ExchangeRateId { get; } = exchangeRateId;
    public CurrencyId FromCurrencyId { get; } = fromCurrencyId;
    public CurrencyId ToCurrencyId { get; } = toCurrencyId;
    public decimal Rate { get; } = rate;
    public Guid CreatedById { get; } = createdById;
}
