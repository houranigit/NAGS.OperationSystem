using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

/// <summary>
/// A single return-to-ramp occurrence and the exact work performed during that occurrence.
/// Child rows retain their work-order foreign key for aggregate ownership and carry this record's
/// id for occurrence grouping.
/// </summary>
public sealed class WorkOrderReturnToRamp : Entity<Guid>
{
    public const int MaxDescriptionLength = 2000;

    private readonly List<WorkOrderServiceLine> _serviceLines = [];
    private readonly List<WorkOrderTask> _tasks = [];

    private WorkOrderReturnToRamp() { }

    internal WorkOrderReturnToRamp(
        Guid id,
        Guid workOrderId,
        WorkOrderReturnToRampInput input,
        Guid recordedByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        WorkOrderId = workOrderId;
        RecordedByUserId = recordedByUserId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ApplyMetadata(input);

        foreach (var line in input.ServiceLines)
            _serviceLines.Add(new WorkOrderServiceLine(Guid.NewGuid(), workOrderId, line, id));
        foreach (var task in input.Tasks)
            _tasks.Add(new WorkOrderTask(Guid.NewGuid(), workOrderId, task with { Id = null }, id));
    }

    public Guid WorkOrderId { get; private set; }
    public TimeWindow Window { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public IReadOnlyList<WorkOrderServiceLine> ServiceLines => _serviceLines.AsReadOnly();
    public IReadOnlyList<WorkOrderTask> Tasks => _tasks.AsReadOnly();

    internal Result Update(WorkOrderReturnToRampInput input)
    {
        var serviceIds = input.ServiceLines.Where(line => line.Id.HasValue).Select(line => line.Id!.Value).ToList();
        if (serviceIds.Count != serviceIds.Distinct().Count())
            return Error.Validation("Return-to-ramp service line ids must be unique.", "Operations.ReturnToRamp.ServiceLineIdsDuplicate");
        if (serviceIds.Any(id => _serviceLines.All(line => line.Id != id)))
            return Error.Conflict("One or more service line ids do not belong to this return-to-ramp record.", "Operations.ReturnToRamp.ServiceLineIdForeign");

        var taskIds = input.Tasks.Where(task => task.Id.HasValue).Select(task => task.Id!.Value).ToList();
        if (taskIds.Count != taskIds.Distinct().Count())
            return Error.Validation("Return-to-ramp task ids must be unique.", "Operations.ReturnToRamp.TaskIdsDuplicate");
        if (taskIds.Any(id => _tasks.All(task => task.Id != id)))
            return Error.Conflict("One or more task ids do not belong to this return-to-ramp record.", "Operations.ReturnToRamp.TaskIdForeign");

        ApplyMetadata(input);

        var existingLines = _serviceLines.ToDictionary(line => line.Id);
        var retainedLines = new HashSet<Guid>();
        foreach (var line in input.ServiceLines)
        {
            if (line.Id is { } id)
            {
                existingLines[id].Update(line);
                retainedLines.Add(id);
            }
            else
            {
                var added = new WorkOrderServiceLine(Guid.NewGuid(), WorkOrderId, line, Id);
                _serviceLines.Add(added);
                retainedLines.Add(added.Id);
            }
        }
        _serviceLines.RemoveAll(line => !retainedLines.Contains(line.Id));

        var existingTasks = _tasks.ToDictionary(task => task.Id);
        var retainedTasks = new HashSet<Guid>();
        foreach (var task in input.Tasks)
        {
            if (task.Id is { } id)
            {
                existingTasks[id].Update(task);
                retainedTasks.Add(id);
            }
            else
            {
                var added = new WorkOrderTask(Guid.NewGuid(), WorkOrderId, task, Id);
                _tasks.Add(added);
                retainedTasks.Add(added.Id);
            }
        }
        _tasks.RemoveAll(task => !retainedTasks.Contains(task.Id));

        return Result.Success();
    }

    private void ApplyMetadata(WorkOrderReturnToRampInput input)
    {
        Window = input.Window;
        Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
    }
}
