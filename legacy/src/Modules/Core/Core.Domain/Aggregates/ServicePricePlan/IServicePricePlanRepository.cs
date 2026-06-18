using Core.Domain.Aggregates.AircraftType;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Aggregates.Service;

namespace Core.Domain.Aggregates.ServicePricePlan;

public interface IServicePricePlanRepository
{
    Task<ServicePricePlan?> GetByIdAsync(ServicePricePlanId id, CancellationToken ct = default);

    Task<bool> ExistsForCombinationAsync(
        ServiceId serviceId,
        OperationTypeId operationTypeId,
        AircraftTypeId? aircraftTypeId,
        ServicePricePlanId? excludeId = null,
        CancellationToken ct = default);

    Task<bool> HasActiveForServiceAsync(ServiceId serviceId, CancellationToken ct = default);
    Task<bool> HasActiveForOperationTypeAsync(OperationTypeId operationTypeId, CancellationToken ct = default);
    Task<bool> HasActiveForAircraftTypeAsync(AircraftTypeId aircraftTypeId, CancellationToken ct = default);
    Task<bool> HasActiveForCurrencyAsync(CurrencyId currencyId, CancellationToken ct = default);

    void Add(ServicePricePlan plan);
    void Update(ServicePricePlan plan);
    void Remove(ServicePricePlan plan);
}
