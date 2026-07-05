using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Common;

/// <summary>
/// Validates MasterData references via the read seam and builds Operations snapshot value objects.
/// All references must exist and be active at capture time.
/// </summary>
public sealed class MasterDataResolver(IMasterDataReader reader)
{
    public async Task<Result<CustomerSnapshot>> CustomerAsync(Guid id, CancellationToken ct)
    {
        var c = await reader.GetCustomerAsync(id, ct);
        if (c is null)
            return Error.Validation("The customer was not found.", "Operations.Ref.CustomerNotFound");
        if (!c.IsActive)
            return Error.Validation("The customer is inactive.", "Operations.Ref.CustomerInactive");
        return new CustomerSnapshot(c.Id, c.IataCode, c.Name);
    }

    public async Task<Result<StationSnapshot>> StationAsync(Guid id, CancellationToken ct)
    {
        var s = await reader.GetStationAsync(id, ct);
        if (s is null)
            return Error.Validation("The station was not found.", "Operations.Ref.StationNotFound");
        if (!s.IsActive)
            return Error.Validation("The station is inactive.", "Operations.Ref.StationInactive");
        return new StationSnapshot(s.Id, s.IataCode, s.Name);
    }

    public async Task<Result<OperationTypeSnapshot>> OperationTypeAsync(Guid id, CancellationToken ct)
    {
        var o = await reader.GetOperationTypeAsync(id, ct);
        if (o is null)
            return Error.Validation("The operation type was not found.", "Operations.Ref.OperationTypeNotFound");
        if (!o.IsActive)
            return Error.Validation("The operation type is inactive.", "Operations.Ref.OperationTypeInactive");
        return new OperationTypeSnapshot(o.Id, o.Name);
    }

    public async Task<Result<AircraftTypeSnapshot?>> AircraftTypeAsync(Guid? id, CancellationToken ct)
    {
        if (id is not { } aircraftTypeId)
            return Result.Success<AircraftTypeSnapshot?>(null);

        var a = await reader.GetAircraftTypeAsync(aircraftTypeId, ct);
        if (a is null)
            return Error.Validation("The aircraft type was not found.", "Operations.Ref.AircraftTypeNotFound");
        if (!a.IsActive)
            return Error.Validation("The aircraft type is inactive.", "Operations.Ref.AircraftTypeInactive");
        return Result.Success<AircraftTypeSnapshot?>(new AircraftTypeSnapshot(a.Id, a.Manufacturer, a.Model));
    }

    public async Task<Result<IReadOnlyList<ServiceSnapshot>>> ServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return Result.Success<IReadOnlyList<ServiceSnapshot>>([]);

        var found = await reader.GetServicesAsync(ids, ct);
        var byId = found.ToDictionary(s => s.Id);

        var snapshots = new List<ServiceSnapshot>();
        foreach (var id in ids.Distinct())
        {
            if (!byId.TryGetValue(id, out var s))
                return Error.Validation($"Service '{id}' was not found.", "Operations.Ref.ServiceNotFound");
            if (!s.IsActive)
                return Error.Validation($"Service '{s.Name}' is inactive.", "Operations.Ref.ServiceInactive");
            snapshots.Add(new ServiceSnapshot(s.Id, s.Name));
        }

        return snapshots;
    }

    public async Task<Result<ServiceSnapshot>> ServiceAsync(Guid id, CancellationToken ct)
    {
        var s = await reader.GetServiceAsync(id, ct);
        if (s is null)
            return Error.Validation($"Service '{id}' was not found.", "Operations.Ref.ServiceNotFound");
        if (!s.IsActive)
            return Error.Validation($"Service '{s.Name}' is inactive.", "Operations.Ref.ServiceInactive");
        return new ServiceSnapshot(s.Id, s.Name);
    }

    public async Task<Result<StaffMemberSnapshot>> StaffMemberAsync(Guid id, CancellationToken ct)
    {
        var s = await reader.GetStaffMemberAsync(id, ct);
        if (s is null)
            return Error.Validation($"Staff member '{id}' was not found.", "Operations.Ref.StaffNotFound");
        if (!s.IsActive)
            return Error.Validation($"Staff member '{s.FullName}' is inactive.", "Operations.Ref.StaffInactive");
        return new StaffMemberSnapshot(s.Id, s.FullName, s.EmployeeId);
    }

    public async Task<Result<IReadOnlyList<StaffMemberSnapshot>>> StaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return Result.Success<IReadOnlyList<StaffMemberSnapshot>>([]);

        var found = await reader.GetStaffMembersAsync(ids, ct);
        var byId = found.ToDictionary(s => s.Id);

        var snapshots = new List<StaffMemberSnapshot>();
        foreach (var id in ids.Distinct())
        {
            if (!byId.TryGetValue(id, out var s))
                return Error.Validation($"Staff member '{id}' was not found.", "Operations.Ref.StaffNotFound");
            if (!s.IsActive)
                return Error.Validation($"Staff member '{s.FullName}' is inactive.", "Operations.Ref.StaffInactive");
            snapshots.Add(new StaffMemberSnapshot(s.Id, s.FullName, s.EmployeeId));
        }

        return snapshots;
    }

    public async Task<Result<IReadOnlyList<StaffMemberSnapshot>>> StaffMembersForStationAsync(
        IReadOnlyCollection<Guid> ids,
        Guid stationId,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return Result.Success<IReadOnlyList<StaffMemberSnapshot>>([]);

        var found = await reader.GetStaffMembersAsync(ids, ct);
        var byId = found.ToDictionary(s => s.Id);

        var snapshots = new List<StaffMemberSnapshot>();
        foreach (var id in ids.Distinct())
        {
            if (!byId.TryGetValue(id, out var s))
                return Error.Validation($"Staff member '{id}' was not found.", "Operations.Ref.StaffNotFound");
            if (!s.IsActive)
                return Error.Validation($"Staff member '{s.FullName}' is inactive.", "Operations.Ref.StaffInactive");
            if (s.StationId != stationId)
                return Error.Validation($"Staff member '{s.FullName}' does not belong to the selected station.", "Operations.Ref.StaffStationMismatch");

            snapshots.Add(new StaffMemberSnapshot(s.Id, s.FullName, s.EmployeeId));
        }

        return snapshots;
    }

    public async Task<Result<ToolSnapshot>> ToolAsync(Guid id, CancellationToken ct)
    {
        var t = await reader.GetToolAsync(id, ct);
        if (t is null || !t.IsActive)
            return Error.Validation($"Tool '{id}' was not found or is inactive.", "Operations.Ref.ToolInvalid");
        return new ToolSnapshot(t.Id, t.Name);
    }

    public async Task<Result<MaterialSnapshot>> MaterialAsync(Guid id, CancellationToken ct)
    {
        var m = await reader.GetMaterialAsync(id, ct);
        if (m is null || !m.IsActive)
            return Error.Validation($"Material '{id}' was not found or is inactive.", "Operations.Ref.MaterialInvalid");
        return new MaterialSnapshot(m.Id, m.Name);
    }

    public async Task<Result<GeneralSupportSnapshot>> GeneralSupportAsync(Guid id, CancellationToken ct)
    {
        var g = await reader.GetGeneralSupportAsync(id, ct);
        if (g is null || !g.IsActive)
            return Error.Validation($"General support '{id}' was not found or is inactive.", "Operations.Ref.GeneralSupportInvalid");
        return new GeneralSupportSnapshot(g.Id, g.Name);
    }
}
