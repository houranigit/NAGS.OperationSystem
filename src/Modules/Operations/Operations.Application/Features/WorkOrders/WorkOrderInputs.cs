using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Resources;
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
    WorkOrderSignatureCommand? CustomerSignature = null,
    IReadOnlyList<WorkOrderReturnToRampCommand>? ReturnToRamps = null);

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

public sealed record WorkOrderReturnToRampCommand(
    Guid? Id,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    IReadOnlyList<WorkOrderServiceLineCommand> ServiceLines,
    IReadOnlyList<WorkOrderTaskCommand> Tasks);

public sealed record WorkOrderTaskToolCommand(
    Guid ToolId,
    decimal? Quantity,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null);

public sealed record WorkOrderTaskMaterialCommand(
    Guid MaterialId,
    decimal? Quantity,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null);

public sealed record WorkOrderTaskGeneralSupportCommand(
    Guid GeneralSupportId,
    decimal? Quantity,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null);

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
    IReadOnlyList<WorkOrderTaskInput> Tasks,
    IReadOnlyList<WorkOrderReturnToRampInput>? ReturnToRamps);

public sealed class WorkOrderInputBuilder(Common.MasterDataResolver resolver)
{
    public async Task<Result<BuiltWorkOrderInput>> BuildAsync(
        WorkOrderEditableCommandPayload payload,
        WorkOrderType type,
        string fallbackFlightNumber,
        Guid stationId,
        CancellationToken cancellationToken,
        bool preserveOmittedReturnToRamps = false)
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

        IReadOnlyList<WorkOrderReturnToRampInput>? returnToRamps;
        if (payload.ReturnToRamps is not null)
        {
            var built = await BuildReturnToRampsAsync(payload.ReturnToRamps, stationId, cancellationToken);
            if (built.IsFailure)
                return built.Error;
            returnToRamps = built.Value;
        }
        else if (preserveOmittedReturnToRamps)
        {
            returnToRamps = null;
        }
        else
        {
            returnToRamps = BuildLegacyReturnToRampInputs(serviceLines.Value, tasks.Value);
        }

        return new BuiltWorkOrderInput(
            actualFlightNumber.Value,
            aircraft.Value,
            payload.AircraftTailNumber,
            actuals.Value,
            cancellation.Value,
            payload.Remarks,
            serviceLines.Value.Where(line => !line.IsReturnToRamp).ToList(),
            tasks.Value.Where(task => !task.IsReturnToRamp).ToList(),
            returnToRamps);
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
            if (!line.IsReturnToRamp && actualArrivalUtc is { } ataUtc && !IsMissing(line.FromUtc) && line.FromUtc.ToUniversalTime() < ataUtc)
                Add($"{prefix}.{nameof(line.FromUtc)}", "Service line From time cannot be before ATA.");
            if (!line.IsReturnToRamp && actualDepartureUtc is { } atdUtc && !IsMissing(line.ToUtc) && line.ToUtc.ToUniversalTime() > atdUtc)
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
            if (!task.IsReturnToRamp && actualArrivalUtc is { } ataUtc && !IsMissing(task.FromUtc) && task.FromUtc.ToUniversalTime() < ataUtc)
                Add($"{prefix}.{nameof(task.FromUtc)}", "Task From time cannot be before ATA.");
            if (!task.IsReturnToRamp && actualDepartureUtc is { } atdUtc && !IsMissing(task.ToUtc) && task.ToUtc.ToUniversalTime() > atdUtc)
                Add($"{prefix}.{nameof(task.ToUtc)}", "Task To time cannot be after ATD.");

            ValidateResourceRows(task.Tools ?? [], $"{prefix}.{nameof(task.Tools)}", "tool", row => row.ToolId, row => row.Quantity, row => row.FromUtc, row => row.ToUtc, Add);
            ValidateResourceRows(task.Materials ?? [], $"{prefix}.{nameof(task.Materials)}", "material", row => row.MaterialId, row => row.Quantity, row => row.FromUtc, row => row.ToUtc, Add);
            ValidateResourceRows(task.GeneralSupports ?? [], $"{prefix}.{nameof(task.GeneralSupports)}", "general support", row => row.GeneralSupportId, row => row.Quantity, row => row.FromUtc, row => row.ToUtc, Add);
        }

        if (payload.ReturnToRamps is not null)
        {
            if (type == WorkOrderType.Cancellation && payload.ReturnToRamps.Count > 0)
                Add(nameof(payload.ReturnToRamps), "Cancellation work orders cannot include return-to-ramp records.");
            if (serviceLines.Any(line => line.IsReturnToRamp) || tasks.Any(task => task.IsReturnToRamp))
                Add(nameof(payload.ReturnToRamps), "Do not mix legacy return-to-ramp flags with return-to-ramp records.");

            for (var returnIndex = 0; returnIndex < payload.ReturnToRamps.Count; returnIndex++)
            {
                var item = payload.ReturnToRamps[returnIndex];
                var prefix = $"{nameof(payload.ReturnToRamps)}[{returnIndex}]";
                if (IsMissing(item.FromUtc))
                    Add($"{prefix}.{nameof(item.FromUtc)}", "Return-to-ramp From time is required.");
                if (IsMissing(item.ToUtc))
                    Add($"{prefix}.{nameof(item.ToUtc)}", "Return-to-ramp To time is required.");
                if (!IsMissing(item.FromUtc) && !IsMissing(item.ToUtc) && item.ToUtc < item.FromUtc)
                    Add($"{prefix}.{nameof(item.ToUtc)}", "Return-to-ramp To time cannot be before From time.");
                if (string.IsNullOrWhiteSpace(item.Description) is false && item.Description.Trim().Length > 2000)
                    Add($"{prefix}.{nameof(item.Description)}", "Return-to-ramp description must be at most 2000 characters.");
                if ((item.ServiceLines?.Count ?? 0) + (item.Tasks?.Count ?? 0) == 0)
                    Add(prefix, "Return to ramp requires at least one service or task.");

                var returnServiceLines = item.ServiceLines ?? [];
                for (var serviceIndex = 0; serviceIndex < returnServiceLines.Count; serviceIndex++)
                {
                    var line = returnServiceLines[serviceIndex];
                    var linePrefix = $"{prefix}.{nameof(item.ServiceLines)}[{serviceIndex}]";
                    if (line.ServiceId == Guid.Empty)
                        Add($"{linePrefix}.{nameof(line.ServiceId)}", "Every service line needs a service.");
                    if (line.PerformedByStaffMemberIds is not { Count: > 0 } || line.PerformedByStaffMemberIds.Any(id => id == Guid.Empty))
                        Add($"{linePrefix}.{nameof(line.PerformedByStaffMemberIds)}", "Every service line needs at least one performer.");
                    if (IsMissing(line.FromUtc) || IsMissing(line.ToUtc))
                        Add(linePrefix, "Every return-to-ramp service needs From and To times.");
                    else if (line.ToUtc < line.FromUtc)
                        Add($"{linePrefix}.{nameof(line.ToUtc)}", "Service line To time cannot be before From time.");
                    else if (line.FromUtc < item.FromUtc || line.ToUtc > item.ToUtc)
                        Add(linePrefix, "Service line times must be inside the return-to-ramp window.");
                }

                var returnTasks = item.Tasks ?? [];
                for (var taskIndex = 0; taskIndex < returnTasks.Count; taskIndex++)
                {
                    var task = returnTasks[taskIndex];
                    var taskPrefix = $"{prefix}.{nameof(item.Tasks)}[{taskIndex}]";
                    if (!Enum.IsDefined(task.TaskType))
                        Add($"{taskPrefix}.{nameof(task.TaskType)}", "Every task needs a valid task type.");
                    if (task.EmployeeIds is not { Count: > 0 } || task.EmployeeIds.Any(id => id == Guid.Empty))
                        Add($"{taskPrefix}.{nameof(task.EmployeeIds)}", "Every task needs at least one employee.");
                    if (IsMissing(task.FromUtc) || IsMissing(task.ToUtc))
                        Add(taskPrefix, "Every return-to-ramp task needs From and To times.");
                    else if (task.ToUtc < task.FromUtc)
                        Add($"{taskPrefix}.{nameof(task.ToUtc)}", "Task To time cannot be before From time.");
                    else if (task.FromUtc < item.FromUtc || task.ToUtc > item.ToUtc)
                        Add(taskPrefix, "Task times must be inside the return-to-ramp window.");

                    ValidateResourceRows(task.Tools ?? [], $"{taskPrefix}.{nameof(task.Tools)}", "tool", row => row.ToolId, row => row.Quantity, row => row.FromUtc, row => row.ToUtc, Add);
                    ValidateResourceRows(task.Materials ?? [], $"{taskPrefix}.{nameof(task.Materials)}", "material", row => row.MaterialId, row => row.Quantity, row => row.FromUtc, row => row.ToUtc, Add);
                    ValidateResourceRows(task.GeneralSupports ?? [], $"{taskPrefix}.{nameof(task.GeneralSupports)}", "general support", row => row.GeneralSupportId, row => row.Quantity, row => row.FromUtc, row => row.ToUtc, Add);
                }
            }
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
        Func<T, decimal?> quantity,
        Func<T, DateTimeOffset?> fromUtc,
        Func<T, DateTimeOffset?> toUtc,
        Action<string, string> add)
    {
        var seenItemIds = new HashSet<Guid>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowItemId = itemId(row);
            if (rowItemId == Guid.Empty)
                add($"{prefix}[{i}].ItemId", $"Every {label} row needs an item.");
            else if (!seenItemIds.Add(rowItemId))
                add($"{prefix}[{i}].ItemId", $"Duplicate {label} rows are not allowed within one task.");
            var rowQuantity = quantity(row);
            var rowFromUtc = fromUtc(row);
            var rowToUtc = toUtc(row);
            if (rowQuantity is <= 0)
                add($"{prefix}[{i}].Quantity", $"{ToTitle(label)} quantities must be greater than zero.");
            if (rowQuantity is { } value &&
                (value > 9999999999999999.99m || decimal.Round(value, 2) != value))
                add($"{prefix}[{i}].Quantity", $"{ToTitle(label)} quantities support up to 16 whole digits and 2 decimal places.");
            if (rowFromUtc is { } from && from == default)
                add($"{prefix}[{i}].FromUtc", $"{ToTitle(label)} From time must be valid.");
            if (rowToUtc is { } to && to == default)
                add($"{prefix}[{i}].ToUtc", $"{ToTitle(label)} To time must be valid when supplied.");
            if (rowToUtc.HasValue && !rowFromUtc.HasValue)
                add($"{prefix}[{i}].FromUtc", $"{ToTitle(label)} From time is required when To is supplied.");
            if (rowFromUtc.HasValue && rowToUtc < rowFromUtc)
                add($"{prefix}[{i}].ToUtc", $"{ToTitle(label)} To time cannot be before From time.");
            if (rowQuantity.HasValue && (rowFromUtc.HasValue || rowToUtc.HasValue))
                add($"{prefix}[{i}]", $"{ToTitle(label)} usage cannot contain both quantity and duration values.");
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

            var tools = await BuildToolsAsync(task.Tools ?? [], window.Value, cancellationToken);
            if (tools.IsFailure)
                return tools.Error;

            var materials = await BuildMaterialsAsync(task.Materials ?? [], window.Value, cancellationToken);
            if (materials.IsFailure)
                return materials.Error;

            var supports = await BuildGeneralSupportsAsync(task.GeneralSupports ?? [], window.Value, cancellationToken);
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

    public async Task<Result<WorkOrderReturnToRampInput>> BuildReturnToRampAsync(
        WorkOrderReturnToRampCommand command,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        if (command.FromUtc == default || command.ToUtc == default)
            return Error.Validation("Return-to-ramp From and To times are required.", "Operations.ReturnToRamp.WindowRequired");
        if (command.ToUtc < command.FromUtc)
            return Error.Validation("Return-to-ramp To time cannot be before From time.", "Operations.ReturnToRamp.WindowInvalid");
        if (string.IsNullOrWhiteSpace(command.Description) is false && command.Description.Trim().Length > 2000)
            return Error.Validation("Return-to-ramp description must be at most 2000 characters.", "Operations.ReturnToRamp.DescriptionTooLong");
        if ((command.ServiceLines?.Count ?? 0) + (command.Tasks?.Count ?? 0) == 0)
            return Error.Validation("Return to ramp requires at least one service or task.", "Operations.ReturnToRamp.ActivityRequired");

        var window = TimeWindow.Create(command.FromUtc, command.ToUtc);
        if (window.IsFailure)
            return window.Error;

        var serviceLines = await BuildServiceLinesAsync(command.ServiceLines ?? [], stationId, cancellationToken);
        if (serviceLines.IsFailure)
            return serviceLines.Error;
        var tasks = await BuildTasksAsync(command.Tasks ?? [], stationId, cancellationToken);
        if (tasks.IsFailure)
            return tasks.Error;

        return new WorkOrderReturnToRampInput(
            command.Id,
            window.Value,
            command.Description,
            serviceLines.Value,
            tasks.Value);
    }

    private async Task<Result<IReadOnlyList<WorkOrderReturnToRampInput>>> BuildReturnToRampsAsync(
        IReadOnlyList<WorkOrderReturnToRampCommand> commands,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderReturnToRampInput>(commands.Count);
        foreach (var command in commands)
        {
            var result = await BuildReturnToRampAsync(command, stationId, cancellationToken);
            if (result.IsFailure)
                return result.Error;
            results.Add(result.Value);
        }

        return results;
    }

    private static IReadOnlyList<WorkOrderReturnToRampInput> BuildLegacyReturnToRampInputs(
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks)
    {
        var legacyServices = serviceLines.Where(line => line.IsReturnToRamp).ToList();
        var legacyTasks = tasks.Where(task => task.IsReturnToRamp).ToList();
        if (legacyServices.Count + legacyTasks.Count == 0)
            return [];

        var from = legacyServices.Select(line => line.Window.From)
            .Concat(legacyTasks.Select(task => task.Window.From))
            .Min();
        var to = legacyServices.Select(line => line.Window.To)
            .Concat(legacyTasks.Select(task => task.Window.To))
            .Max();
        var window = TimeWindow.Create(from, to);
        return window.IsFailure
            ? []
            : [new WorkOrderReturnToRampInput(null, window.Value, null, legacyServices, legacyTasks)];
    }

    private async Task<Result<IReadOnlyList<WorkOrderTaskToolInput>>> BuildToolsAsync(
        IReadOnlyList<WorkOrderTaskToolCommand> items,
        TimeWindow taskWindow,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderTaskToolInput>(items.Count);
        foreach (var item in items)
        {
            var tool = await resolver.ToolAsync(item.ToolId, cancellationToken);
            if (tool.IsFailure)
                return tool.Error;

            var usage = BuildResourceUsage(
                tool.Value.CalculationType,
                item.Quantity,
                item.FromUtc,
                item.ToUtc,
                taskWindow);
            if (usage.IsFailure)
                return usage.Error;

            results.Add(new WorkOrderTaskToolInput(tool.Value, usage.Value));
        }

        return results;
    }

    private async Task<Result<IReadOnlyList<WorkOrderTaskMaterialInput>>> BuildMaterialsAsync(
        IReadOnlyList<WorkOrderTaskMaterialCommand> items,
        TimeWindow taskWindow,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderTaskMaterialInput>(items.Count);
        foreach (var item in items)
        {
            var material = await resolver.MaterialAsync(item.MaterialId, cancellationToken);
            if (material.IsFailure)
                return material.Error;

            var usage = BuildResourceUsage(
                material.Value.CalculationType,
                item.Quantity,
                item.FromUtc,
                item.ToUtc,
                taskWindow);
            if (usage.IsFailure)
                return usage.Error;

            results.Add(new WorkOrderTaskMaterialInput(material.Value, usage.Value));
        }

        return results;
    }

    private async Task<Result<IReadOnlyList<WorkOrderTaskGeneralSupportInput>>> BuildGeneralSupportsAsync(
        IReadOnlyList<WorkOrderTaskGeneralSupportCommand> items,
        TimeWindow taskWindow,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkOrderTaskGeneralSupportInput>(items.Count);
        foreach (var item in items)
        {
            var support = await resolver.GeneralSupportAsync(item.GeneralSupportId, cancellationToken);
            if (support.IsFailure)
                return support.Error;

            var usage = BuildResourceUsage(
                support.Value.CalculationType,
                item.Quantity,
                item.FromUtc,
                item.ToUtc,
                taskWindow);
            if (usage.IsFailure)
                return usage.Error;

            results.Add(new WorkOrderTaskGeneralSupportInput(support.Value, usage.Value));
        }

        return results;
    }

    private static Result<ResourceUsage> BuildResourceUsage(
        ResourceCalculationType calculationType,
        decimal? quantity,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        TimeWindow taskWindow)
    {
        // Rolling compatibility for clients/outbox items created before duration usage existed:
        // their quantity-only duration resources inherit the owning task's closed window.
        if (calculationType == ResourceCalculationType.Duration &&
            quantity.HasValue &&
            !fromUtc.HasValue &&
            !toUtc.HasValue)
        {
            quantity = null;
            fromUtc = taskWindow.From;
            toUtc = taskWindow.To;
        }

        var usage = ResourceUsage.Create(calculationType, quantity, fromUtc, toUtc);
        if (usage.IsFailure)
            return usage.Error;
        if (calculationType == ResourceCalculationType.Duration && usage.Value.FromUtc < taskWindow.From)
            return Error.Validation(
                "Resource usage From time cannot be before its task From time.",
                "Operations.ResourceUsage.FromBeforeTask");
        if (calculationType == ResourceCalculationType.Duration && usage.Value.ToUtc > taskWindow.To)
            return Error.Validation(
                "Resource usage To time cannot be after its task To time.",
                "Operations.ResourceUsage.ToAfterTask");

        return usage;
    }
}
