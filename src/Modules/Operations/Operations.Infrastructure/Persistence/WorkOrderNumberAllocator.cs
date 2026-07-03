using System.Data;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Domain.Sequences;

namespace Operations.Infrastructure.Persistence;

/// <summary>
/// Allocates per-station work-order sequence numbers under a serializable transaction. This is the
/// explicit exception to optimistic concurrency for human-facing business numbers.
/// </summary>
public sealed class WorkOrderNumberAllocator(OperationsDbContext db) : IWorkOrderNumberAllocator
{
    public async Task<int> NextAsync(Guid stationId, string stationIata, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var sequence = await db.StationWorkOrderSequences.FirstOrDefaultAsync(s => s.Id == stationId, cancellationToken);
        if (sequence is null)
        {
            sequence = new StationWorkOrderSequence(stationId, stationIata);
            db.StationWorkOrderSequences.Add(sequence);
        }

        var value = sequence.Next();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return value;
    }
}
