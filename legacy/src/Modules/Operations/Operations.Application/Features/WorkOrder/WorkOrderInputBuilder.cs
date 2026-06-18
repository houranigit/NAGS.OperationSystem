using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.Employee;
using Core.Contracts.Readers;
using Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Entities;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;
using Store.Contracts.Readers;

namespace Operations.Application.Features.WorkOrder;

/// <summary>
/// Resolves Core/Store snapshots for the work-order line and task inputs supplied by the
/// portal/mobile UIs. Shared between create / update / record-RTR command handlers.
/// </summary>
public sealed class WorkOrderInputBuilder(
    IServiceReader serviceReader,
    IEmployeeReader employeeReader,
    IToolReader toolReader,
    IMaterialReader materialReader,
    IGeneralSupportReader generalSupportReader)
{
    /// <summary>
    /// Resolves service + employee snapshots for the supplied service-line inputs. Forces
    /// <see cref="WorkOrderServiceLineInput.ReturnToRamp"/> to <paramref name="forceReturnToRamp"/>
    /// when set (used by RTR append).
    /// </summary>
    public async Task<Result<IReadOnlyList<WorkOrderServiceLineInput>>> BuildServiceLinesAsync(
        IReadOnlyList<CreateWorkOrderServiceLineInput>? lines,
        bool forceReturnToRamp,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
            return Result<IReadOnlyList<WorkOrderServiceLineInput>>.Success(Array.Empty<WorkOrderServiceLineInput>());

        var serviceIds = lines.Select(l => l.ServiceId).Distinct().ToList();
        var services = (await serviceReader.GetManyAsync(serviceIds, cancellationToken)).ToDictionary(s => s.ServiceId);

        var built = new List<WorkOrderServiceLineInput>(lines.Count);
        foreach (var line in lines)
        {
            if (!services.TryGetValue(line.ServiceId, out var serviceSnapshot))
                return Result<IReadOnlyList<WorkOrderServiceLineInput>>.Failure(
                    Error.Validation($"Service '{line.ServiceId}' was not found."));

            var employeeSnapshot = await employeeReader.GetByIdAsync(line.EmployeeId, cancellationToken);
            if (employeeSnapshot is null)
                return Result<IReadOnlyList<WorkOrderServiceLineInput>>.Failure(
                    Error.Validation($"Employee '{line.EmployeeId}' was not found."));

            built.Add(new WorkOrderServiceLineInput(
                serviceSnapshot,
                employeeSnapshot,
                line.From,
                line.To,
                line.Description,
                forceReturnToRamp || line.ReturnToRamp));
        }

        return Result<IReadOnlyList<WorkOrderServiceLineInput>>.Success(built);
    }

    /// <summary>
    /// Resolves employee / tool / material / general-support snapshots for each task and
    /// returns the domain task input shape. Forces <see cref="WorkOrderTaskInput.ReturnToRamp"/>
    /// to <paramref name="forceReturnToRamp"/> when set (used by RTR append).
    /// </summary>
    public async Task<Result<IReadOnlyList<WorkOrderTaskInput>>> BuildTasksAsync(
        IReadOnlyList<CreateWorkOrderTaskInput>? tasks,
        bool forceReturnToRamp,
        CancellationToken cancellationToken)
    {
        if (tasks is null || tasks.Count == 0)
            return Result<IReadOnlyList<WorkOrderTaskInput>>.Success(Array.Empty<WorkOrderTaskInput>());

        // Pre-resolve unique ids in a few round-trips, then index in-memory.
        var allEmployeeIds = tasks.SelectMany(t => t.EmployeeIds ?? Array.Empty<Guid>()).Distinct().ToList();
        var allToolIds = tasks.SelectMany(t => t.ToolIds ?? Array.Empty<Guid>()).Distinct().ToList();
        var allMaterialIds = tasks.SelectMany(t => t.MaterialIds ?? Array.Empty<Guid>()).Distinct().ToList();
        var allGsIds = tasks.SelectMany(t => t.GeneralSupportIds ?? Array.Empty<Guid>()).Distinct().ToList();

        var employees = new Dictionary<Guid, EmployeeSnapshot>();
        foreach (var id in allEmployeeIds)
        {
            var e = await employeeReader.GetByIdAsync(id, cancellationToken);
            if (e is null)
                return Result<IReadOnlyList<WorkOrderTaskInput>>.Failure(
                    Error.Validation($"Employee '{id}' was not found."));
            employees[id] = e;
        }

        var tools = (await toolReader.GetManyAsync(allToolIds, cancellationToken)).ToDictionary(t => t.ToolId);
        if (tools.Count != allToolIds.Count)
        {
            var missing = allToolIds.Where(id => !tools.ContainsKey(id)).ToList();
            return Result<IReadOnlyList<WorkOrderTaskInput>>.Failure(
                Error.Validation($"Tools not found: {string.Join(", ", missing)}."));
        }

        var materials = (await materialReader.GetManyAsync(allMaterialIds, cancellationToken)).ToDictionary(m => m.MaterialId);
        if (materials.Count != allMaterialIds.Count)
        {
            var missing = allMaterialIds.Where(id => !materials.ContainsKey(id)).ToList();
            return Result<IReadOnlyList<WorkOrderTaskInput>>.Failure(
                Error.Validation($"Materials not found: {string.Join(", ", missing)}."));
        }

        var generalSupports = (await generalSupportReader.GetManyAsync(allGsIds, cancellationToken)).ToDictionary(g => g.GeneralSupportId);
        if (generalSupports.Count != allGsIds.Count)
        {
            var missing = allGsIds.Where(id => !generalSupports.ContainsKey(id)).ToList();
            return Result<IReadOnlyList<WorkOrderTaskInput>>.Failure(
                Error.Validation($"General supports not found: {string.Join(", ", missing)}."));
        }

        var built = new List<WorkOrderTaskInput>(tasks.Count);
        foreach (var t in tasks)
        {
            var taskEmployees = (t.EmployeeIds ?? Array.Empty<Guid>())
                .Select(id => employees[id])
                .ToList();
            var taskTools = (t.ToolIds ?? Array.Empty<Guid>())
                .Select(id => tools[id])
                .ToList();
            var taskMaterials = (t.MaterialIds ?? Array.Empty<Guid>())
                .Select(id => materials[id])
                .ToList();
            var taskGs = (t.GeneralSupportIds ?? Array.Empty<Guid>())
                .Select(id => generalSupports[id])
                .ToList();

            var attachments = (t.Attachments ?? Array.Empty<CreateWorkOrderTaskAttachmentInput>())
                .Select(a => new TaskAttachmentInput(a.Kind, a.ContentType, a.FileName, a.Bytes, a.CapturedAt))
                .ToList();

            built.Add(new WorkOrderTaskInput(
                t.TaskType,
                t.Description,
                t.From,
                t.To,
                forceReturnToRamp || t.ReturnToRamp,
                taskEmployees,
                taskTools,
                taskMaterials,
                taskGs,
                attachments));
        }

        return Result<IReadOnlyList<WorkOrderTaskInput>>.Success(built);
    }
}
