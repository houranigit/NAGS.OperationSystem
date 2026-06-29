using BuildingBlocks.Application.Messaging;
using MasterData.Domain.AircraftTypes;
using MasterData.Domain.Countries;
using MasterData.Domain.Customers;
using MasterData.Domain.GeneralSupports;
using MasterData.Domain.Licenses;
using MasterData.Domain.ManpowerTypes;
using MasterData.Domain.Materials;
using MasterData.Domain.OperationTypes;
using MasterData.Domain.Services;
using MasterData.Domain.StaffMembers;
using MasterData.Domain.Stations;
using MasterData.Domain.Tools;
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

    public DbSet<Service> Services { get; }

    public DbSet<OperationType> OperationTypes { get; }

    public DbSet<AircraftType> AircraftTypes { get; }

    public DbSet<Tool> Tools { get; }

    public DbSet<Equipment> ToolEquipments { get; }

    public DbSet<Material> Materials { get; }

    public DbSet<GeneralSupport> GeneralSupports { get; }

    public DbSet<Station> Stations { get; }

    public DbSet<Customer> Customers { get; }

    public DbSet<CustomerContact> CustomerContacts { get; }

    public DbSet<StaffMember> StaffMembers { get; }

    public DbSet<StaffMemberLicense> StaffMemberLicenses { get; }

    /// <summary>Sets the original concurrency token so a stale update fails with a concurrency conflict.</summary>
    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class;
}
