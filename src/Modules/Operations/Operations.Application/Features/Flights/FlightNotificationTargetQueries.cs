using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public sealed record GetFlightNotificationTargetsQuery(IReadOnlyList<Guid> FlightIds, bool MobileOnly = false)
    : IQuery<IReadOnlyList<Guid>>;

/// <summary>Checks notification links without loading flight details or widening either client's read scope.</summary>
public sealed class GetFlightNotificationTargetsQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IMobileFlightScope mobileScope) : IQueryHandler<GetFlightNotificationTargetsQuery, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(
        GetFlightNotificationTargetsQuery request, CancellationToken cancellationToken)
    {
        if (request.FlightIds.Count > 100)
            return Error.Validation("Check no more than 100 notification flight targets at a time.",
                "Operations.Flight.NotificationTargetsLimit");

        IQueryable<Flight> query = db.Flights.AsNoTracking();
        if (request.MobileOnly)
        {
            var resolved = await mobileScope.ResolveAsync(cancellationToken);
            if (resolved.IsFailure)
                return resolved.Error;
            query = MobileFlightQuery.ApplyReadScope(query, resolved.Value);
        }
        else
        {
            var resolved = await scope.ResolveAsync(cancellationToken);
            if (resolved.IsFailure)
                return resolved.Error;
            query = FlightListQuery.ApplyScopeAndFilters(query, resolved.Value,
                new FlightListFilter(null, null, null, null, null, null));
        }

        var ids = request.FlightIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            return Array.Empty<Guid>();

        var accessibleIds = await query.Where(flight => ids.Contains(flight.Id))
            .Select(flight => flight.Id).ToListAsync(cancellationToken);
        return accessibleIds;
    }
}
