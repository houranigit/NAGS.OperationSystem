using System.Data;
using System.Reflection;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Messaging;
using MasterData.Application.Abstractions;
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
using Microsoft.EntityFrameworkCore.Storage;

namespace MasterData.Infrastructure.Persistence;

public sealed class MasterDataDbContext(DbContextOptions<MasterDataDbContext> options)
    : DbContext(options), IMasterDataDbContext, IOutboxDbContext
{
    public const string Schema = "masterdata";

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<ManpowerType> ManpowerTypes => Set<ManpowerType>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<OperationType> OperationTypes => Set<OperationType>();
    public DbSet<AircraftType> AircraftTypes => Set<AircraftType>();
    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<Equipment> ToolEquipments => Set<Equipment>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<GeneralSupport> GeneralSupports => Set<GeneralSupport>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffMemberLicense> StaffMemberLicenses => Set<StaffMemberLicense>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public async Task<IMasterDataTransaction> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken = default) =>
        new MasterDataTransaction(
            await Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
        Entry(entity).Property("RowVersion").OriginalValue = rowVersion;

    private sealed class MasterDataTransaction(IDbContextTransaction transaction) : IMasterDataTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyOutboxInbox();
        base.OnModelCreating(modelBuilder);
    }
}
