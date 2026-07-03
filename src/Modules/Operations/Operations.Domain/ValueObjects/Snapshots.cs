using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

// Immutable point-in-time snapshots of MasterData-owned reference data. Operations stores these
// inside its aggregates (owned types) instead of live foreign keys, preserving historical accuracy.

public sealed class CustomerSnapshot : ValueObject
{
    private CustomerSnapshot() { }

    public CustomerSnapshot(Guid customerId, string? iataCode, string name)
    {
        CustomerId = customerId;
        IataCode = iataCode;
        Name = name;
    }

    public Guid CustomerId { get; private set; }
    public string? IataCode { get; private set; }
    public string Name { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CustomerId;
        yield return IataCode;
        yield return Name;
    }
}

public sealed class StationSnapshot : ValueObject
{
    private StationSnapshot() { }

    public StationSnapshot(Guid stationId, string iataCode, string name)
    {
        StationId = stationId;
        IataCode = iataCode;
        Name = name;
    }

    public Guid StationId { get; private set; }
    public string IataCode { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return StationId;
        yield return IataCode;
        yield return Name;
    }
}

public sealed class OperationTypeSnapshot : ValueObject
{
    private OperationTypeSnapshot() { }

    public OperationTypeSnapshot(Guid operationTypeId, string name)
    {
        OperationTypeId = operationTypeId;
        Name = name;
    }

    public Guid OperationTypeId { get; private set; }
    public string Name { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return OperationTypeId;
        yield return Name;
    }
}

public sealed class AircraftTypeSnapshot : ValueObject
{
    private AircraftTypeSnapshot() { }

    public AircraftTypeSnapshot(Guid aircraftTypeId, string manufacturer, string model)
    {
        AircraftTypeId = aircraftTypeId;
        Manufacturer = manufacturer;
        Model = model;
    }

    public Guid AircraftTypeId { get; private set; }
    public string Manufacturer { get; private set; } = null!;
    public string Model { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return AircraftTypeId;
        yield return Manufacturer;
        yield return Model;
    }
}

public sealed class ServiceSnapshot : ValueObject
{
    private ServiceSnapshot() { }

    public ServiceSnapshot(Guid serviceId, string name)
    {
        ServiceId = serviceId;
        Name = name;
    }

    public Guid ServiceId { get; private set; }
    public string Name { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ServiceId;
        yield return Name;
    }
}

public sealed class StaffMemberSnapshot : ValueObject
{
    private StaffMemberSnapshot() { }

    public StaffMemberSnapshot(Guid staffMemberId, string fullName, string employeeId)
    {
        StaffMemberId = staffMemberId;
        FullName = fullName;
        EmployeeId = employeeId;
    }

    public Guid StaffMemberId { get; private set; }
    public string FullName { get; private set; } = null!;
    public string EmployeeId { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return StaffMemberId;
        yield return FullName;
        yield return EmployeeId;
    }
}

public sealed class ToolSnapshot : ValueObject
{
    private ToolSnapshot() { }

    public ToolSnapshot(Guid toolId, string name)
    {
        ToolId = toolId;
        Name = name;
    }

    public Guid ToolId { get; private set; }
    public string Name { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ToolId;
        yield return Name;
    }
}

public sealed class MaterialSnapshot : ValueObject
{
    private MaterialSnapshot() { }

    public MaterialSnapshot(Guid materialId, string name)
    {
        MaterialId = materialId;
        Name = name;
    }

    public Guid MaterialId { get; private set; }
    public string Name { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaterialId;
        yield return Name;
    }
}

public sealed class GeneralSupportSnapshot : ValueObject
{
    private GeneralSupportSnapshot() { }

    public GeneralSupportSnapshot(Guid generalSupportId, string name)
    {
        GeneralSupportId = generalSupportId;
        Name = name;
    }

    public Guid GeneralSupportId { get; private set; }
    public string Name { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return GeneralSupportId;
        yield return Name;
    }
}
