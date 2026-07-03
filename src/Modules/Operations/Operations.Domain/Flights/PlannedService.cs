using BuildingBlocks.Domain.Entities;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A service planned for a flight. Today selected manually; later sourced from the contract.</summary>
public sealed class PlannedService : Entity<Guid>
{
    private PlannedService() { }

    internal PlannedService(Guid id, Guid flightId, ServiceSnapshot service)
    {
        Id = id;
        FlightId = flightId;
        Service = service;
    }

    public Guid FlightId { get; private set; }
    public ServiceSnapshot Service { get; private set; } = null!;

    /// <summary>True when this planned service is the "Aircraft Per Landing" designation (no intended service).</summary>
    public bool IsAircraftPerLanding => Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService;
}
