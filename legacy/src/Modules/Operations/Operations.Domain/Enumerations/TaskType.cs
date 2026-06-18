namespace Operations.Domain.Enumerations;

/// <summary>
/// Severity classification of a <see cref="Operations.Domain.Entities.WorkOrderTask"/>.
/// Mirrors the previous corrective-action types (<c>Major</c> / <c>Minor</c>) — the unified
/// task model replaces both the legacy corrective-action and employee-line concepts.
/// </summary>
public enum TaskType
{
    Major = 0,
    Minor = 1,
}
