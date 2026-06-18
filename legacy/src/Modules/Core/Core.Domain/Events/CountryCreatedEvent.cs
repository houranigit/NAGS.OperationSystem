using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Country;

namespace Core.Domain.Events;

public sealed class CountryCreatedEvent(CountryId countryId) : DomainEvent
{
    public CountryId CountryId { get; } = countryId;
}
