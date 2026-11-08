using BuildingBlocks.Domain.ValueObjects;
using MasterData.Contracts.Resources;

namespace Operations.Domain.ValueObjects;

// Immutable snapshots of MasterData references used by actual task records.

public sealed class AtaChapterSnapshot : ValueObject
{
    private AtaChapterSnapshot() { }

    public AtaChapterSnapshot(Guid ataChapterId, string code, string title)
    {
        AtaChapterId = ataChapterId;
        Code = code;
        Title = title;
    }

    public Guid AtaChapterId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return AtaChapterId;
        yield return Code;
        yield return Title;
    }
}

public sealed class ToolSnapshot : ValueObject
{
    private ToolSnapshot() { }

    public ToolSnapshot(Guid toolId, string name, ResourceCalculationType calculationType = ResourceCalculationType.Duration)
    {
        ToolId = toolId;
        Name = name;
        CalculationType = calculationType;
    }

    public Guid ToolId { get; private set; }
    public string Name { get; private set; } = null!;
    public ResourceCalculationType CalculationType { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ToolId;
        yield return Name;
        yield return CalculationType;
    }
}

public sealed class MaterialSnapshot : ValueObject
{
    private MaterialSnapshot() { }

    public MaterialSnapshot(Guid materialId, string name, ResourceCalculationType calculationType = ResourceCalculationType.Quantity)
    {
        MaterialId = materialId;
        Name = name;
        CalculationType = calculationType;
    }

    public Guid MaterialId { get; private set; }
    public string Name { get; private set; } = null!;
    public ResourceCalculationType CalculationType { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaterialId;
        yield return Name;
        yield return CalculationType;
    }
}

public sealed class GeneralSupportSnapshot : ValueObject
{
    private GeneralSupportSnapshot() { }

    public GeneralSupportSnapshot(Guid generalSupportId, string name, ResourceCalculationType calculationType = ResourceCalculationType.Quantity)
    {
        GeneralSupportId = generalSupportId;
        Name = name;
        CalculationType = calculationType;
    }

    public Guid GeneralSupportId { get; private set; }
    public string Name { get; private set; } = null!;
    public ResourceCalculationType CalculationType { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return GeneralSupportId;
        yield return Name;
        yield return CalculationType;
    }
}
