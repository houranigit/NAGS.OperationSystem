using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>Point-in-time copy of a Store tool.</summary>
public sealed class ToolSnapshot : ValueObject
{
    public Guid ToolId { get; private set; }
    public string Name { get; private set; } = null!;

    private ToolSnapshot() { }

    private ToolSnapshot(Guid toolId, string name)
    {
        ToolId = toolId;
        Name = name;
    }

    public static Result<ToolSnapshot> Create(Guid toolId, string? name)
    {
        if (toolId == Guid.Empty)
            return Error.Validation("ToolId is required.");

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Tool name is required.");

        if (name.Length > 100)
            return Error.Validation("Tool name must not exceed 100 characters.");

        return new ToolSnapshot(toolId, name.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return ToolId;
        yield return Name;
    }
}
