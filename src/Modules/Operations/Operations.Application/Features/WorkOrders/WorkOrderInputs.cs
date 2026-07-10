using BuildingBlocks.Domain.Results;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public sealed record WorkOrderEditableCommandPayload(
    string? ActualFlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    DateTimeOffset? CanceledAtUtc,
    string? CancellationReason,
    string? Remarks,
    IReadOnlyList<WorkOrderServiceLineCommand> ServiceLines,
    IReadOnlyList<WorkOrderTaskCommand> Tasks);

public sealed record WorkOrderServiceLineCommand(
    Guid ServiceId,
    Guid PerformedByStaffMemberId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description);

public sealed record WorkOrderTaskCommand(
    Guid? Id,
    TaskType TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<Guid> EmployeeIds,
    IReadOnlyList<WorkOrderTaskToolCommand> Tools,
    IReadOnlyList<WorkOrderTaskMaterialCommand> Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportCommand> GeneralSupports);

public sealed record WorkOrderTaskToolCommand(Guid ToolId, decimal Quantity);

public sealed record WorkOrderTaskMaterialCommand(Guid MaterialId, decimal Quantity);

public sealed record WorkOrderTaskGeneralSupportCommand(Guid GeneralSupportId, decimal Quantity);

public sealed record BuiltWorkOrderInput(
    FlightNumber ActualFlightNumber,
    AircraftTypeSnapshot? AircraftType,
    string? AircraftTailNumber,
    ActualTime? Actuals,
    CancellationDetails? Cancellation,
    string? Remarks,
    IReadOnlyList<WorkOrderServiceLineInput> ServiceLines,
    IReadOnlyList<WorkOrderTaskInput> Tasks);

public sealed class WorkOrderInputBuilder(Common.MasterDataResolver resolver)
{
    public async Task<Result<BuiltWorkOrderInput>> BuildAsync(
        WorkOrderEditableCommandPayload payload,
        string fallbackFlightNumber,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        var actualFlightNumber = FlightNumber.Create(
            string.IsNullOrWhiteSpace(payload.ActualFlightNumber) ? fallbackFlightNumber : payload.ActualFlightNumber);
        if (actualFlightNumber.IsFailure)
            return actualFlightNumber.Error;

        var aircraft = await resolver.AircraftTypeAsync(payload.AircraftTypeId, cancellationToken);
        if (aircraft.IsFailure)
            return aircraft.Error;

        var actuals = BuildActuals(payload.ActualArrivalUtc, payload.ActualDepartureUtc);
        if (actuals.IsFailure)
            return actuals.Error;

        var cancellation = BuildCancellation(payload.CanceledAtUtc, payload.CancellationReason);
        if (cancellation.IsFailure)
            return cancellation.Error;

        var serviceLines = await BuildServiceLinesAsync(payload.ServiceLines ?? [], stationId, cancellationToken);
        if (serviceLines.IsFailure)
            return serviceLines.Error;

        var tasks = await BuildTasksAsync(payload.Tasks ?? [], stationId, cancellationToken);
        if (tasks.IsFailure)
            return tasks.Error;

        return new BuiltWorkOrderInput(
            actualFlightNumber.Value,
            aircraft.Value,
            payload.AircraftTailNumber,
            actuals.Value,
            cancellation.Value,
            payload.Remarks,
            serviceLines.Value,
            tasks.Value);
    }

    private static Result<ActualTime?> BuildActuals(DateTimeOffset? ata, DateTimeOffset? atd)
    {
        if (ata is null && atd is null)
            return Result.Success<ActualTime?>(null);
        if (ata is null || atd is null)
            return Error.Validation("Both actual arrival and departure are required when actuals are supplied.", "Operations.WorkOrder.ActualsIncomplete");

        var actuals = ActualTime.Create(ata.Value, atd.Value);
        return actuals.IsFailure ? actuals.Error : Result.Success<ActualTime?>(actuals.Value);
    }

    private static Result<CancellationDetails?> BuildCancellation(DateTimeOffset? canceledAtUtc, string? reason)
    {
        if (canceledAtUtc is null)
            return Result.Success<CancellationDetails?>(null);

        var cancellation = CancellationDetails.Create(canceledAtUtc.Value, reason);
        return cancellation.IsFailure ? cancellation.Error : Result.Success<CancellationDetails?>(cancellation.Value);
    }

    private async Task<Result<IReadOnlyList<WorkOrderServiceLineInput>>> BuildServiceLinesAsync(
        IReadOnlyList<WorkOrderServiceLineCommand> lines,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderServiceLineInput>(lines.Count);
        foreach (var line in lines)
        {
            var service = await resolver.ServiceAsync(line.ServiceId, cancellationToken);
            if (service.IsFailure)
                return service.Error;

            var staff = await resolver.StaffMembersForStationAsync([line.PerformedByStaffMemberId], stationId, cancellationToken);
            if (staff.IsFailure)
                return staff.Error;

            var window = TimeWindow.Create(line.FromUtc, line.ToUtc);
            if (window.IsFailure)
                return window.Error;

            results.Add(new WorkOrderServiceLineInput(service.Value, staff.Value[0], window.Value, line.Description));
        }

        return results;
    }

    private async Task<Result<IReadOnlyList<WorkOrderTaskInput>>> BuildTasksAsync(
        IReadOnlyList<WorkOrderTaskCommand> tasks,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderTaskInput>(tasks.Count);
        foreach (var task in tasks)
        {
            var window = TimeWindow.Create(task.FromUtc, task.ToUtc);
            if (window.IsFailure)
                return window.Error;

            var employees = await resolver.StaffMembersForStationAsync(task.EmployeeIds ?? [], stationId, cancellationToken);
            if (employees.IsFailure)
                return employees.Error;

            var tools = await BuildToolsAsync(task.Tools ?? [], cancellationToken);
            if (tools.IsFailure)
                return tools.Error;

            var materials = await BuildMaterialsAsync(task.Materials ?? [], cancellationToken);
            if (materials.IsFailure)
                return materials.Error;

            var supports = await BuildGeneralSupportsAsync(task.GeneralSupports ?? [], cancellationToken);
            if (supports.IsFailure)
                return supports.Error;

            results.Add(new WorkOrderTaskInput(
                task.Id,
                task.TaskType,
                task.Description,
                window.Value,
                employees.Value,
                tools.Value,
                materials.Value,
                supports.Value));
        }

        return results;
    }

    private async Task<Result<IReadOnlyList<WorkOrderTaskToolInput>>> BuildToolsAsync(
        IReadOnlyList<WorkOrderTaskToolCommand> items,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderTaskToolInput>(items.Count);
        foreach (var item in items)
        {
            var tool = await resolver.ToolAsync(item.ToolId, cancellationToken);
            if (tool.IsFailure)
                return tool.Error;

            var quantity = Quantity.Create(item.Quantity);
            if (quantity.IsFailure)
                return quantity.Error;

            results.Add(new WorkOrderTaskToolInput(tool.Value, quantity.Value));
        }

        return results;
    }

    private async Task<Result<IReadOnlyList<WorkOrderTaskMaterialInput>>> BuildMaterialsAsync(
        IReadOnlyList<WorkOrderTaskMaterialCommand> items,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderTaskMaterialInput>(items.Count);
        foreach (var item in items)
        {
            var material = await resolver.MaterialAsync(item.MaterialId, cancellationToken);
            if (material.IsFailure)
                return material.Error;

            var quantity = Quantity.Create(item.Quantity);
            if (quantity.IsFailure)
                return quantity.Error;

            results.Add(new WorkOrderTaskMaterialInput(material.Value, quantity.Value));
        }

        return results;
    }

    private async Task<Result<IReadOnlyList<WorkOrderTaskGeneralSupportInput>>> BuildGeneralSupportsAsync(
        IReadOnlyList<WorkOrderTaskGeneralSupportCommand> items,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderTaskGeneralSupportInput>(items.Count);
        foreach (var item in items)
        {
            var support = await resolver.GeneralSupportAsync(item.GeneralSupportId, cancellationToken);
            if (support.IsFailure)
                return support.Error;

            var quantity = Quantity.Create(item.Quantity);
            if (quantity.IsFailure)
                return quantity.Error;

            results.Add(new WorkOrderTaskGeneralSupportInput(support.Value, quantity.Value));
        }

        return results;
    }
}
