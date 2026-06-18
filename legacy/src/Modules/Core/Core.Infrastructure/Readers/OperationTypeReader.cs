using Core.Contracts.Features.OperationType;
using Core.Contracts.Readers;
using Core.Domain.Aggregates.OperationType;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Readers;

internal sealed class OperationTypeReader(CoreDbContext context) : IOperationTypeReader
{
    public async Task<OperationTypeSnapshot?> GetByIdAsync(
        Guid operationTypeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.OperationTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OperationTypeId.From(operationTypeId), cancellationToken);

        return entity is null
            ? null
            : new OperationTypeSnapshot(entity.Id.Value, entity.Name);
    }

    public Task<bool> ExistsActiveAsync(Guid operationTypeId, CancellationToken cancellationToken = default)
    {
        var typedId = OperationTypeId.From(operationTypeId);
        return context.OperationTypes
            .AsNoTracking()
            .AnyAsync(o => o.Id == typedId && o.IsActive, cancellationToken);
    }
}
