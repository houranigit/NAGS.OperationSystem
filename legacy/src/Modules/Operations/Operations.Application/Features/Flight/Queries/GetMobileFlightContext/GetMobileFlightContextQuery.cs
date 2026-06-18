using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Operations.Contracts.Mobile;

namespace Operations.Application.Features.Flight.Queries.GetMobileFlightContext;

/// <summary>
/// Bundles the data the mobile flight-actions screen needs to dispatch to the right
/// downstream flow (Create / Cancel / Update / Return-to-ramp). Always scoped to the
/// caller's own work order — other employees' work orders never leak into the payload.
/// </summary>
public sealed record GetMobileFlightContextQuery(
    Guid FlightId,
    Guid EmployeeId) : IQuery<MobileFlightContextDto>;
