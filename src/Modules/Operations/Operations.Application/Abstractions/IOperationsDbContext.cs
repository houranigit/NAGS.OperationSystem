using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;

namespace Operations.Application.Abstractions;

public interface IOperationsDbContext : IOutboxDbContext
{
    public DbSet<Flight> Flights { get; }
    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class;
}
