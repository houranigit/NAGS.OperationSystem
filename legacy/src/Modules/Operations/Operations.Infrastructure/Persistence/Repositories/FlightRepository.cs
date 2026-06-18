using Microsoft.EntityFrameworkCore;
using Operations.Domain.Aggregates.Flight;
using FlightEntity = Operations.Domain.Aggregates.Flight.Flight;

namespace Operations.Infrastructure.Persistence.Repositories;

public sealed class FlightRepository(OperationsDbContext context) : IFlightRepository
{
    public async Task<FlightEntity?> GetByIdAsync(FlightId id, CancellationToken cancellationToken = default) =>
        await context.Flights
            .Include(f => f.AssignedEmployees)
            .Include(f => f.AttachedWorkOrderLinks)
            .Include(f => f.Services)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public void Add(FlightEntity flight) => context.Flights.Add(flight);

    /// <summary>
    /// Command handlers normally load flights through <see cref="GetByIdAsync"/> (already tracked).
    /// Calling <c>DbSet.Update</c> on a tracked aggregate recursively forces dependents into
    /// <c>Modified</c>; brand-new navigations such as <c>FlightWorkOrderAttachment</c> then emit
    /// UPDATE ... WHERE Id = @newId (0 rows) and EF throws DbUpdateConcurrencyException.
    /// Only attach detached roots (tests / edge paths), otherwise EF change tracking is enough.
    /// </summary>
    public void Update(FlightEntity flight)
    {
        if (context.Entry(flight).State == EntityState.Detached)
            context.Flights.Update(flight);
    }
}
