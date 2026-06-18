using BuildingBlocks.Application.Abstractions;
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

namespace Core.Application.Abstractions;

/// <summary>Read/query surface for Core aggregate projection; implemented by persistence.</summary>
public interface ICoreDbContext : IUnitOfWork
{
    IQueryable<AircraftType> AircraftTypes { get; }
    IQueryable<Country> Countries { get; }
    IQueryable<Currency> Currencies { get; }
    IQueryable<ExchangeRate> ExchangeRates { get; }
    IQueryable<Customer> Customers { get; }
    IQueryable<CustomerContact> CustomerContacts { get; }
    IQueryable<Employee> Employees { get; }
    IQueryable<EmployeeLicense> EmployeeLicenses { get; }
    IQueryable<License> Licenses { get; }
    IQueryable<ManpowerType> ManpowerTypes { get; }
    IQueryable<OperationType> OperationTypes { get; }
    IQueryable<Service> Services { get; }
    IQueryable<Station> Stations { get; }
    IQueryable<ManpowerPricePlan> ManpowerPricePlans { get; }
    IQueryable<ServicePricePlan> ServicePricePlans { get; }

    /// <summary>True if an inbox row already exists for the given EventId — handlers should short-circuit.</summary>
    Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an inbox row keyed by <paramref name="eventId"/>. The next SaveChanges persists it; a
    /// re-delivery raises a unique-key conflict so the handler is naturally idempotent.
    /// </summary>
    void MarkProcessed(Guid eventId, string eventType);
}
