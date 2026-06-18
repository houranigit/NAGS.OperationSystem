using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.License;

namespace Core.Domain.Events;

public sealed class LicenseDeactivatedEvent(LicenseId licenseId) : DomainEvent
{
    public LicenseId LicenseId { get; } = licenseId;
}
