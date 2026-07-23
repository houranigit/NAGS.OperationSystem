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
    IReadOnlyList<WorkOrderTaskCommand> Tasks,
    WorkOrderSignatureCommand? CustomerSignature = null);

public sealed record WorkOrderServiceLineCommand(
    Guid ServiceId,
    IReadOnlyList<Guid> PerformedByStaffMemberIds,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    bool IsReturnToRamp = false,
    Guid? Id = null,
    IReadOnlyList<WorkOrderServiceLineAttachmentCommand>? Attachments = null);

public sealed record WorkOrderTaskCommand(
    Guid? Id,
    TaskType TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<Guid> EmployeeIds,
    IReadOnlyList<WorkOrderTaskToolCommand> Tools,
    IReadOnlyList<WorkOrderTaskMaterialCommand> Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportCommand> GeneralSupports,
    IReadOnlyList<WorkOrderTaskAttachmentCommand>? Attachments = null,
    bool IsReturnToRamp = false);

public sealed record WorkOrderTaskToolCommand(Guid ToolId, decimal Quantity);

public sealed record WorkOrderTaskMaterialCommand(Guid MaterialId, decimal Quantity);

public sealed record WorkOrderTaskGeneralSupportCommand(Guid GeneralSupportId, decimal Quantity);

public sealed record WorkOrderTaskAttachmentCommand(
    TaskAttachmentKind Kind,
    string Base64Content,
    string FileName,
    string ContentType);

public sealed record WorkOrderServiceLineAttachmentCommand(
    TaskAttachmentKind Kind,
    string Base64Content,
    string FileName,
    string ContentType);

public sealed record WorkOrderSignatureCommand(
    string Base64Content,
    string FileName,
    string ContentType);

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
        WorkOrderType type,
        string fallbackFlightNumber,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        var validation = ValidatePayload(payload, type);
        if (validation.IsFailure)
            return validation.Error;

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

    private static Result ValidatePayload(WorkOrderEditableCommandPayload payload, WorkOrderType type)
    {
        var failures = new Dictionary<string, List<string>>();

        void Add(string field, string message)
        {
            if (!failures.TryGetValue(field, out var messages))
            {
                messages = [];
                failures[field] = messages;
            }

            messages.Add(message);
        }

        if (type == WorkOrderType.Cancellation)
        {
            if (IsMissing(payload.CanceledAtUtc))
                Add(nameof(payload.CanceledAtUtc), "Cancellation work orders require a cancellation time.");
            if (string.IsNullOrWhiteSpace(payload.CancellationReason))
                Add(nameof(payload.CancellationReason), "Cancellation work orders require a reason.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(payload.ActualFlightNumber))
                Add(nameof(payload.ActualFlightNumber), "Flight number is required.");
            if (payload.AircraftTypeId is null || payload.AircraftTypeId == Guid.Empty)
                Add(nameof(payload.AircraftTypeId), "Aircraft type is required.");
            if (IsMissing(payload.ActualArrivalUtc))
                Add(nameof(payload.ActualArrivalUtc), "ATA is required.");
            if (IsMissing(payload.ActualDepartureUtc))
                Add(nameof(payload.ActualDepartureUtc), "ATD is required.");
        }

        var hasAta = !IsMissing(payload.ActualArrivalUtc);
        var hasAtd = !IsMissing(payload.ActualDepartureUtc);
        if (payload.ActualArrivalUtc is { } ata && ata == default)
            Add(nameof(payload.ActualArrivalUtc), "ATA must be a valid time.");
        if (payload.ActualDepartureUtc is { } atd && atd == default)
            Add(nameof(payload.ActualDepartureUtc), "ATD must be a valid time.");
        if (hasAta != hasAtd)
            Add(nameof(payload.ActualArrivalUtc), "Provide both ATA and ATD, or leave both blank until approval.");
        if (hasAta && hasAtd && payload.ActualDepartureUtc < payload.ActualArrivalUtc)
            Add(nameof(payload.ActualDepartureUtc), "ATD cannot be before ATA.");
        var actualArrivalUtc = payload.ActualArrivalUtc?.ToUniversalTime();
        var actualDepartureUtc = payload.ActualDepartureUtc?.ToUniversalTime();

        var serviceLines = payload.ServiceLines ?? [];
        for (var i = 0; i < serviceLines.Count; i++)
        {
            var line = serviceLines[i];
            var prefix = $"{nameof(payload.ServiceLines)}[{i}]";
            if (line.ServiceId == Guid.Empty)
                Add($"{prefix}.{nameof(line.ServiceId)}", "Every service line needs a service.");
            if (line.PerformedByStaffMemberIds is not { Count: > 0 })
                Add($"{prefix}.{nameof(line.PerformedByStaffMemberIds)}", "Every service line needs at least one performer.");
            else if (line.PerformedByStaffMemberIds.Any(id => id == Guid.Empty))
                Add($"{prefix}.{nameof(line.PerformedByStaffMemberIds)}", "Service line performers must be selected.");
            if (IsMissing(line.FromUtc))
                Add($"{prefix}.{nameof(line.FromUtc)}", "Every service line needs a From time.");
            if (IsMissing(line.ToUtc))
                Add($"{prefix}.{nameof(line.ToUtc)}", "Every service line needs a To time.");
            if (!IsMissing(line.FromUtc) && !IsMissing(line.ToUtc) && line.ToUtc < line.FromUtc)
                Add($"{prefix}.{nameof(line.ToUtc)}", "Service line To time cannot be before From time.");
            if (actualArrivalUtc is { } ataUtc && !IsMissing(line.FromUtc) && line.FromUtc.ToUniversalTime() < ataUtc)
                Add($"{prefix}.{nameof(line.FromUtc)}", "Service line From time cannot be before ATA.");
            if (actualDepartureUtc is { } atdUtc && !IsMissing(line.ToUtc) && line.ToUtc.ToUniversalTime() > atdUtc)
                Add($"{prefix}.{nameof(line.ToUtc)}", "Service line To time cannot be after ATD.");
        }

        var tasks = payload.Tasks ?? [];
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var prefix = $"{nameof(payload.Tasks)}[{i}]";
            if (!Enum.IsDefined(task.TaskType))
                Add($"{prefix}.{nameof(task.TaskType)}", "Every task needs a valid task type.");
            if (task.EmployeeIds is not { Count: > 0 })
                Add($"{prefix}.{nameof(task.EmployeeIds)}", "Every task needs at least one employee.");
            else if (task.EmployeeIds.Any(id => id == Guid.Empty))
                Add($"{prefix}.{nameof(task.EmployeeIds)}", "Task employees must be selected.");
            if (IsMissing(task.FromUtc))
                Add($"{prefix}.{nameof(task.FromUtc)}", "Every task needs a From time.");
            if (IsMissing(task.ToUtc))
                Add($"{prefix}.{nameof(task.ToUtc)}", "Every task needs a To time.");
            if (!IsMissing(task.FromUtc) && !IsMissing(task.ToUtc) && task.ToUtc < task.FromUtc)
                Add($"{prefix}.{nameof(task.ToUtc)}", "Task To time cannot be before From time.");
            if (actualArrivalUtc is { } ataUtc && !IsMissing(task.FromUtc) && task.FromUtc.ToUniversalTime() < ataUtc)
                Add($"{prefix}.{nameof(task.FromUtc)}", "Task From time cannot be before ATA.");
            if (actualDepartureUtc is { } atdUtc && !IsMissing(task.ToUtc) && task.ToUtc.ToUniversalTime() > atdUtc)
                Add($"{prefix}.{nameof(task.ToUtc)}", "Task To time cannot be after ATD.");

            ValidateResourceRows(task.Tools ?? [], $"{prefix}.{nameof(task.Tools)}", "tool", row => row.ToolId, row => row.Quantity, Add);
            ValidateResourceRows(task.Materials ?? [], $"{prefix}.{nameof(task.Materials)}", "material", row => row.MaterialId, row => row.Quantity, Add);
            ValidateResourceRows(task.GeneralSupports ?? [], $"{prefix}.{nameof(task.GeneralSupports)}", "general support", row => row.GeneralSupportId, row => row.Quantity, Add);
        }

        if (failures.Count == 0)
            return Result.Success();

        return Error.Validation(
            failures.ToDictionary(pair => pair.Key, pair => pair.Value.Distinct().ToArray()),
            "Please fix the work order before saving.",
            "Operations.WorkOrder.Validation");
    }

    private static void ValidateResourceRows<T>(
        IReadOnlyList<T> rows,
        string prefix,
        string label,
        Func<T, Guid> itemId,
        Func<T, decimal> quantity,
        Action<string, string> add)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (itemId(row) == Guid.Empty)
                add($"{prefix}[{i}].ItemId", $"Every {label} row needs an item.");
            if (quantity(row) <= 0)
                add($"{prefix}[{i}].Quantity", $"{ToTitle(label)} quantities must be greater than zero.");
        }
    }

    private static bool IsMissing(DateTimeOffset? value) => value is null || value.Value == default;

    private static bool IsMissing(DateTimeOffset value) => value == default;

    private static string ToTitle(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

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

            var staff = await resolver.StaffMembersForStationAsync(line.PerformedByStaffMemberIds ?? [], stationId, cancellationToken);
            if (staff.IsFailure)
                return staff.Error;

            var window = TimeWindow.Create(line.FromUtc, line.ToUtc);
            if (window.IsFailure)
                return window.Error;

            results.Add(new WorkOrderServiceLineInput(
                service.Value,
                staff.Value,
                window.Value,
                line.Description,
                line.IsReturnToRamp,
                line.Id));
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
                supports.Value,
                task.IsReturnToRamp));
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
