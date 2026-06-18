using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.License;

namespace Core.Domain.Events;

public sealed class LicenseActivatedEvent(LicenseId licenseId) : DomainEvent
{
    public LicenseId LicenseId { get; } = licenseId;
}
