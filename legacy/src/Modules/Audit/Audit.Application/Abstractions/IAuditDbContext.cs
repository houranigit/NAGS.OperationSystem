using BuildingBlocks.Application.Abstractions;

namespace Audit.Application.Abstractions;

/// <summary>Abstraction implemented by Audit infrastructure persistence.</summary>
public interface IAuditDbContext : IUnitOfWork
{
}
