using Core.Contracts.Features.Customer;
using Core.Contracts.Readers;
using Core.Domain.Aggregates.Customer;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Readers;

internal sealed class CustomerReader(CoreDbContext context) : ICustomerReader
{
    public async Task<CustomerSnapshot?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CustomerId.From(customerId), cancellationToken);

        return entity is null
            ? null
            : new CustomerSnapshot(entity.Id.Value, entity.IataCode.Value, entity.Name);
    }

    public async Task<IReadOnlyList<CustomerSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> customerIds,
        CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0)
            return [];

        var typedIds = customerIds.Select(CustomerId.From).ToList();

        var entities = await context.Customers
            .AsNoTracking()
            .Where(c => typedIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        return entities
            .Select(c => new CustomerSnapshot(c.Id.Value, c.IataCode.Value, c.Name))
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        return await context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CustomerSnapshot(c.Id.Value, c.IataCode.Value, c.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsActiveAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var typedId = CustomerId.From(customerId);
        return context.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == typedId && c.IsActive, cancellationToken);
    }
}
