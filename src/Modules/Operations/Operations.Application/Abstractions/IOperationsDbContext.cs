using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;

namespace Operations.Application.Abstractions;

public interface IOperationsDbContext
{
    public DbSet<Flight> Flights { get; }
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class;
}
