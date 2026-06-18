using BuildingBlocks.Application.Abstractions.Queries;
using Operations.Contracts.Mobile;

namespace Operations.Application.Features.Mobile.Queries.GetMobileBootstrap;

/// <summary>
/// One-shot bootstrap call for the mobile client: returns the lookup bundle plus every
/// flight (summary + context) the calling employee is rostered on, plus every AOG flight
/// at the caller's station, both inside a rolling time window. Reduces the cold-start
/// round-trip cost from <c>1 + 1 + N + 1 + M</c> (me, lookups, my-flights, per-flight
/// context, aog-flights) down to a single request.
/// </summary>
/// <param name="EmployeeId">The caller — resolved from the bearer token in the endpoint.</param>
/// <param name="StationId">
/// Caller's station — needed both to scope the lookup roster and to fetch the station's
/// AOG flights (any employee at the station may serve them).
/// </param>
/// <param name="WindowHours">±hours around <c>UtcNow</c> to consider for assigned and AOG flights.</param>
public sealed record GetMobileBootstrapQuery(
    Guid EmployeeId,
    Guid StationId,
    int WindowHours = 12) : IQuery<MobileBootstrapDto>;
