using BuildingBlocks.Domain.Entities;
using Core.Contracts.Features.Service;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Domain.Entities;

/// <summary>
/// One contract service applicable to a flight. Materialised at flight creation time by
/// copying the resolved contract's OT-services list (see <c>IContractReadService</c>) so
/// the per-flight billable services are immutable from that point forward, even if the
/// contract's OT-services list changes later.
/// </summary>
public sealed class FlightService : Entity<Guid>
{
    public FlightId FlightId { get; private set; } = null!;
    public ServiceSnapshot Service { get; private set; } = null!;

    private FlightService()
    {
    }

    internal FlightService(Guid id, FlightId flightId, ServiceSnapshot service)
    {
        Id = id;
        FlightId = flightId;
        Service = service;
    }
}
