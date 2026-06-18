using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>Point-in-time copy of a Store material.</summary>
public sealed class MaterialSnapshot : ValueObject
{
    public Guid MaterialId { get; private set; }
    public string Name { get; private set; } = null!;

    private MaterialSnapshot() { }

    private MaterialSnapshot(Guid materialId, string name)
    {
        MaterialId = materialId;
        Name = name;
    }

    public static Result<MaterialSnapshot> Create(Guid materialId, string? name)
    {
        if (materialId == Guid.Empty)
            return Error.Validation("MaterialId is required.");

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Material name is required.");

        if (name.Length > 100)
            return Error.Validation("Material name must not exceed 100 characters.");

        return new MaterialSnapshot(materialId, name.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return MaterialId;
        yield return Name;
    }
}
