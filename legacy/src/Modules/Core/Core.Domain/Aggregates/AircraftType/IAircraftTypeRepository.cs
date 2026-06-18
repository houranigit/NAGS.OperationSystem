using Core.Domain.Enumerations;

namespace Core.Domain.Aggregates.AircraftType;

public interface IAircraftTypeRepository
{
    Task<AircraftType?> GetByIdAsync(AircraftTypeId id, CancellationToken ct = default);
    Task<IReadOnlyList<AircraftType>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AircraftType>> GetAllActiveAsync(CancellationToken ct = default);

    Task<bool> ExistsByManufacturerAndModelAsync(Manufacturer manufacturer, string model, AircraftTypeId? excludeId = null, CancellationToken ct = default);

    void Add(AircraftType aircraftType);
    void Update(AircraftType aircraftType);
}
