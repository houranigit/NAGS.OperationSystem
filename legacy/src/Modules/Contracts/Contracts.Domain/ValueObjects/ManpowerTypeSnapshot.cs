using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>Point-in-time copy of a Core manpower type.</summary>
public sealed class ManpowerTypeSnapshot : ValueObject
{
    public Guid ManpowerTypeId { get; private set; }
    public string Name { get; private set; } = null!;

    private ManpowerTypeSnapshot() { }

    private ManpowerTypeSnapshot(Guid manpowerTypeId, string name)
    {
        ManpowerTypeId = manpowerTypeId;
        Name = name;
    }

    public static Result<ManpowerTypeSnapshot> Create(Guid manpowerTypeId, string? name)
    {
        if (manpowerTypeId == Guid.Empty)
            return Error.Validation("ManpowerTypeId is required.");

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Manpower type name is required.");

        if (name.Length > 100)
            return Error.Validation("Manpower type name must not exceed 100 characters.");

        return new ManpowerTypeSnapshot(manpowerTypeId, name.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return ManpowerTypeId;
        yield return Name;
    }
}
