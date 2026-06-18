namespace Store.Contracts.Features.Unit;

/// <summary>Lean read-model used by Tool / Material / GeneralSupport DTOs.</summary>
public sealed record UnitSnapshot(
    Guid UnitId,
    string Code,
    string Name);
