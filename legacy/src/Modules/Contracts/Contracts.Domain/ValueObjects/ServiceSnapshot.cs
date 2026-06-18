using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Point-in-time copy of a Core service. <see cref="IsAog"/> is computed by the application
/// layer (by comparing the resolved id to the well-known AOG seed id) so this Domain stays
/// agnostic of the Core seed constants.
/// </summary>
public sealed class ServiceSnapshot : ValueObject
{
    public Guid ServiceId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsAog { get; private set; }

    private ServiceSnapshot() { }

    private ServiceSnapshot(Guid serviceId, string name, bool isAog)
    {
        ServiceId = serviceId;
        Name = name;
        IsAog = isAog;
    }

    public static Result<ServiceSnapshot> Create(Guid serviceId, string? name, bool isAog)
    {
        if (serviceId == Guid.Empty)
            return Error.Validation("ServiceId is required.");

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Service name is required.");

        if (name.Length > 100)
            return Error.Validation("Service name must not exceed 100 characters.");

        return new ServiceSnapshot(serviceId, name.Trim(), isAog);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return ServiceId;
        yield return Name;
        yield return IsAog;
    }
}
