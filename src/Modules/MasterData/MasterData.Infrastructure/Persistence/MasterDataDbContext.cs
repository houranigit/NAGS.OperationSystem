using System.Reflection;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Messaging;
using MasterData.Application.Abstractions;
using MasterData.Domain.Countries;
using MasterData.Domain.Customers;
using MasterData.Domain.Licenses;
using MasterData.Domain.ManpowerTypes;
using MasterData.Domain.StaffMembers;
using MasterData.Domain.Stations;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Infrastructure.Persistence;

public sealed class MasterDataDbContext(DbContextOptions<MasterDataDbContext> options)
    : DbContext(options), IMasterDataDbContext, IOutboxDbContext
{
    public const string Schema = "masterdata";

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<ManpowerType> ManpowerTypes => Set<ManpowerType>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffMemberLicense> StaffMemberLicenses => Set<StaffMemberLicense>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
        Entry(entity).Property("RowVersion").OriginalValue = rowVersion;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyOutboxInbox();
        base.OnModelCreating(modelBuilder);
    }
}
