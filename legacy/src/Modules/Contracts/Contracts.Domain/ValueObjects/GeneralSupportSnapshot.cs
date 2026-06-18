using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>Point-in-time copy of a Store general-support item.</summary>
public sealed class GeneralSupportSnapshot : ValueObject
{
    public Guid GeneralSupportId { get; private set; }
    public string Name { get; private set; } = null!;

    private GeneralSupportSnapshot() { }

    private GeneralSupportSnapshot(Guid generalSupportId, string name)
    {
        GeneralSupportId = generalSupportId;
        Name = name;
    }

    public static Result<GeneralSupportSnapshot> Create(Guid generalSupportId, string? name)
    {
        if (generalSupportId == Guid.Empty)
            return Error.Validation("GeneralSupportId is required.");

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("General support name is required.");

        if (name.Length > 100)
            return Error.Validation("General support name must not exceed 100 characters.");

        return new GeneralSupportSnapshot(generalSupportId, name.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return GeneralSupportId;
        yield return Name;
    }
}
