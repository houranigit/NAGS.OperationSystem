using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Currency;

namespace Core.Domain.Events;

public sealed class CurrencyDeactivatedEvent(CurrencyId currencyId) : DomainEvent
{
    public CurrencyId CurrencyId { get; } = currencyId;
}
