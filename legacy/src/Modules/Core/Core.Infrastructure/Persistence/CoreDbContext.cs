using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Persistence.Models;
using Core.Application.Abstractions;
using Core.Domain.Aggregates.AircraftType;
using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.Customer;
using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.License;
using Core.Domain.Aggregates.ManpowerPricePlan;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Aggregates.Service;
using Core.Domain.Aggregates.ServicePricePlan;
using Core.Domain.Aggregates.Station;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence;

public sealed class CoreDbContext(
    DbContextOptions<CoreDbContext> options,
    ICurrentUserService currentUserService,
    IPublisher publisher)
    : BaseDbContext(options, currentUserService, publisher), ICoreDbContext
{
    protected override string SchemaName => "core";

    // Temporary: Audit.Infrastructure is not yet ported to the new system. Until it is,
    // CoreDbContext owns the audit.AuditTrails table so BaseDbContext.CaptureAuditTrail can persist.
    // When Audit.Infrastructure is added, remove this override.
    protected override bool ExcludeAuditTrailsFromMigrations => false;

    public DbSet<AircraftType> AircraftTypes => Set<AircraftType>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeLicense> EmployeeLicenses => Set<EmployeeLicense>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<ManpowerType> ManpowerTypes => Set<ManpowerType>();
    public DbSet<OperationType> OperationTypes => Set<OperationType>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<ManpowerPricePlan> ManpowerPricePlans => Set<ManpowerPricePlan>();
    public DbSet<ServicePricePlan> ServicePricePlans => Set<ServicePricePlan>();

    // ICoreDbContext — explicit implementations for read-only query access
    IQueryable<AircraftType> ICoreDbContext.AircraftTypes => AircraftTypes;
    IQueryable<Country> ICoreDbContext.Countries => Countries;
    IQueryable<Currency> ICoreDbContext.Currencies => Currencies;
    IQueryable<ExchangeRate> ICoreDbContext.ExchangeRates => ExchangeRates;
    IQueryable<Customer> ICoreDbContext.Customers => Customers;
    IQueryable<CustomerContact> ICoreDbContext.CustomerContacts => CustomerContacts;
    IQueryable<Employee> ICoreDbContext.Employees => Employees;
    IQueryable<EmployeeLicense> ICoreDbContext.EmployeeLicenses => EmployeeLicenses;
    IQueryable<License> ICoreDbContext.Licenses => Licenses;
    IQueryable<ManpowerType> ICoreDbContext.ManpowerTypes => ManpowerTypes;
    IQueryable<OperationType> ICoreDbContext.OperationTypes => OperationTypes;
    IQueryable<Service> ICoreDbContext.Services => Services;
    IQueryable<Station> ICoreDbContext.Stations => Stations;
    IQueryable<ManpowerPricePlan> ICoreDbContext.ManpowerPricePlans => ManpowerPricePlans;
    IQueryable<ServicePricePlan> ICoreDbContext.ServicePricePlans => ServicePricePlans;

    public async Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await InboxMessages.AnyAsync(m => m.Id == eventId, cancellationToken);

    public void MarkProcessed(Guid eventId, string eventType)
    {
        // PK == EventId so a re-delivery from the outbox processor raises a unique-key conflict
        // instead of double-applying the change (e.g. trying to LinkToUser twice).
        InboxMessages.Add(new InboxMessage
        {
            Id = eventId,
            Type = eventType,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreDbContext).Assembly);
    }
}
