using System.Data;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.StationWorkOrderSequence;

namespace Operations.Infrastructure.Persistence.Repositories;

public sealed class StationWorkOrderSequenceRepository(OperationsDbContext context) : IStationWorkOrderSequenceRepository
{
    public async Task<long> GetNextAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var row = await context.StationWorkOrderCounters
            .SingleOrDefaultAsync(x => x.StationId == stationId, cancellationToken);

        if (row is null)
        {
            row = new StationWorkOrderCounter { StationId = stationId, LastSequence = 0 };
            context.StationWorkOrderCounters.Add(row);
        }

        row.LastSequence++;
        await context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return row.LastSequence;
    }
}
