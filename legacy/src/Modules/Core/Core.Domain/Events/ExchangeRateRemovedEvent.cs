using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Currency;

namespace Core.Domain.Events;

public sealed class ExchangeRateRemovedEvent(
    CurrencyId fromCurrencyId,
    ExchangeRateId exchangeRateId) : DomainEvent
{
    public CurrencyId FromCurrencyId { get; } = fromCurrencyId;
    public ExchangeRateId ExchangeRateId { get; } = exchangeRateId;
}
