using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Abstractions;

/// <summary>
/// Application-facing view of the Identity persistence store. Implemented by the module's
/// EF Core DbContext in Infrastructure so handlers can query/persist without depending on
/// Infrastructure directly.
/// </summary>
public interface IIdentityDbContext
{
    public DbSet<User> Users { get; }
    public DbSet<Role> Roles { get; }
    public DbSet<UserSession> Sessions { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
