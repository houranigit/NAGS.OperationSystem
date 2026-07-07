namespace MasterData.Contracts.Readers;

// Lightweight, point-in-time read projections MasterData exposes to other modules (e.g. Operations)
// so they can validate references and capture immutable snapshots without referencing MasterData
// Domain/Infrastructure. These are read DTOs, not the consuming module's stored snapshot value objects.

public sealed record CustomerReadSnapshot(Guid Id, string? IataCode, string? IcaoCode, string Name, bool IsActive);

public sealed record StationReadSnapshot(Guid Id, string IataCode, string? IcaoCode, string Name, bool IsActive);

public sealed record OperationTypeReadSnapshot(Guid Id, string Name, bool IsActive);

public sealed record AircraftTypeReadSnapshot(Guid Id, string Manufacturer, string Model, bool IsActive);

public sealed record ServiceReadSnapshot(Guid Id, string Name, bool IsActive);

public sealed record StaffMemberReadSnapshot(Guid Id, string FullName, string EmployeeId, Guid StationId, Guid ManpowerTypeId, bool IsActive);

public sealed record ToolReadSnapshot(Guid Id, string Name, bool IsActive);

public sealed record MaterialReadSnapshot(Guid Id, string Name, bool IsActive);

public sealed record GeneralSupportReadSnapshot(Guid Id, string Name, bool IsActive);

/// <summary>
/// Cross-module read seam over MasterData catalogs. Implemented in MasterData.Infrastructure and
/// registered by the MasterData module. Consuming modules depend only on this contract.
/// </summary>
public interface IMasterDataReader
{
    public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken);

    public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken);

    public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken);

    public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken);

    public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken);

    public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken cancellationToken);

    public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(Guid stationId, CancellationToken cancellationToken);

    public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken);

    public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken);

    public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken);
}
