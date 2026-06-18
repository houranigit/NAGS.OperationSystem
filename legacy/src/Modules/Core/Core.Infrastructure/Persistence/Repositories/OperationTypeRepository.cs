using Core.Domain.Aggregates.OperationType;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class OperationTypeRepository(CoreDbContext context) : IOperationTypeRepository
{
    public async Task<OperationType?> GetByIdAsync(OperationTypeId id, CancellationToken ct = default) =>
        await context.OperationTypes.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<OperationType>> GetAllAsync(CancellationToken ct = default) =>
        await context.OperationTypes.ToListAsync(ct);

    public async Task<IReadOnlyList<OperationType>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.OperationTypes.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return await context.OperationTypes.AnyAsync(x => x.Name == trimmed, ct);
    }

    public void Add(OperationType operationType) => context.OperationTypes.Add(operationType);
    public void Update(OperationType operationType) => context.OperationTypes.Update(operationType);
}
