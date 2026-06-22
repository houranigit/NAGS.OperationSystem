using BuildingBlocks.Application.Messaging;
using MasterData.Domain.Countries;
using MasterData.Domain.Customers;
using MasterData.Domain.Licenses;
using MasterData.Domain.ManpowerTypes;
using MasterData.Domain.StaffMembers;
using MasterData.Domain.Stations;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Abstractions;

/// <summary>
/// Application-facing view of the MasterData persistence store, implemented by the module DbContext
/// in Infrastructure. Handlers query/persist through this instead of depending on Infrastructure.
/// Extends <see cref="IOutboxDbContext"/> so integration-event handlers can dedupe via the inbox and
/// enqueue integration events in the same transaction as their state change.
/// </summary>
public interface IMasterDataDbContext : IOutboxDbContext
{
    public DbSet<Country> Countries { get; }

    public DbSet<ManpowerType> ManpowerTypes { get; }

    public DbSet<License> Licenses { get; }

    public DbSet<Station> Stations { get; }

    public DbSet<Customer> Customers { get; }

    public DbSet<CustomerContact> CustomerContacts { get; }

    public DbSet<StaffMember> StaffMembers { get; }

    public DbSet<StaffMemberLicense> StaffMemberLicenses { get; }

    /// <summary>Sets the original concurrency token so a stale update fails with a concurrency conflict.</summary>
    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class;
}
