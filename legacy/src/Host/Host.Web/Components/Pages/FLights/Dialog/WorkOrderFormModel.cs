using Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;
using Operations.Application.Features.WorkOrder.Commands.UpdateWorkOrder;
using Operations.Contracts.Flight;
using Operations.Contracts.WorkOrder;
using Operations.Domain.Enumerations;

namespace Host.Web.Components.Pages.FLights.Dialog;

/// <summary>
/// UI form state for the Work Order Add/Edit dialogs. Mirrors the slim shape of
/// <see cref="CreateWorkOrderForFlightCommand"/> (and <see cref="UpdateWorkOrderCommand"/>):
/// only carries primitives, lookups resolve at submit time. Customer / station /
/// operation type / STA / STD are inherited from the parent flight and shown
/// read-only — they are <b>not</b> stored here because the command pulls them
/// straight off the flight aggregate.
/// </summary>
public sealed class WorkOrderFormModel
{
    public Guid FlightId { get; init; }
    public Guid? WorkOrderId { get; init; }

    public string FlightNumber { get; set; } = "";
    public Guid? AircraftTypeId { get; set; }
    public string? AircraftTailNumber { get; set; }
    public bool IsCanceled { get; set; }
    public DateTime? CancellationAtLocal { get; set; }
    public DateTime? AtaLocal { get; set; }
    public DateTime? AtdLocal { get; set; }
    public string? Remarks { get; set; }

    public List<WorkOrderServiceLineRow> ServiceLines { get; set; } = [];

    /// <summary>
    /// Unified task list — replaces the legacy <c>EmployeeLines</c> + <c>CorrectiveActions</c>
    /// pair. Each task carries its own type, time window, employees, optional store usage,
    /// attachments, and RTR flag.
    /// </summary>
    public List<WorkOrderTaskRow> Tasks { get; set; } = [];

    public bool IsHeaderValid()
    {
        if (string.IsNullOrWhiteSpace(FlightNumber))
            return false;
        if (IsCanceled)
            return CancellationAtLocal.HasValue;
        return AtaLocal.HasValue && AtdLocal.HasValue && AtdLocal >= AtaLocal;
    }

    /// <summary>
    /// Lines are optional; this only enforces that whatever rows the user added are individually valid.
    /// </summary>
    public bool AllLineRowsValid() =>
        ServiceLines.All(l => l.IsValid()) &&
        Tasks.All(t => t.IsValid());

    public static WorkOrderFormModel ForFlight(FlightDto flight) => new()
    {
        FlightId = flight.Id,
        FlightNumber = flight.FlightNumber,
        AircraftTypeId = flight.AircraftTypeId?.AircraftTypeId,
        AtaLocal = flight.Sta.LocalDateTime,
        AtdLocal = flight.Std.LocalDateTime
    };

    /// <summary>
    /// Hydrates a form model from a loaded work order. Used by the edit flow so the dialog
    /// shows the current values; the dialog then turns the model back into an
    /// <see cref="UpdateWorkOrderCommand"/> on save.
    /// </summary>
    public static WorkOrderFormModel ForExisting(WorkOrderDetailDto wo) => new()
    {
        FlightId = wo.FlightSnapshot?.FlightId ?? Guid.Empty,
        WorkOrderId = wo.Id,
        FlightNumber = wo.FlightNumber,
        AircraftTypeId = wo.AircraftTypeSnapshot?.AircraftTypeId,
        AircraftTailNumber = wo.AircraftTailNumber,
        IsCanceled = wo.IsCanceled,
        CancellationAtLocal = wo.CanceledAt?.LocalDateTime,
        AtaLocal = wo.Ata?.LocalDateTime,
        AtdLocal = wo.Atd?.LocalDateTime,
        Remarks = wo.Remarks,
        ServiceLines = wo.ServiceLines
            .Select(l => new WorkOrderServiceLineRow
            {
                ServiceId = l.ServiceSnapshot.ServiceId,
                EmployeeId = l.EmployeeSnapshot.EmployeeId,
                FromLocal = l.From.LocalDateTime,
                ToLocal = l.To.LocalDateTime,
                Description = l.Description,
                ReturnToRamp = l.ReturnToRamp
            })
            .ToList(),
        Tasks = wo.Tasks
            .Select(t => new WorkOrderTaskRow
            {
                TaskType = t.TaskType,
                Description = t.Description,
                FromLocal = t.From.LocalDateTime,
                ToLocal = t.To.LocalDateTime,
                ReturnToRamp = t.ReturnToRamp,
                EmployeeIds = t.Employees.Select(e => e.EmployeeId).ToList(),
                ToolIds = t.Tools.Select(x => x.ToolId).ToList(),
                MaterialIds = t.Materials.Select(x => x.MaterialId).ToList(),
                GeneralSupportIds = t.GeneralSupports.Select(x => x.GeneralSupportId).ToList(),
            })
            .ToList()
    };

    public CreateWorkOrderForFlightCommand ToCreateCommand()
    {
        var (services, tasks) = BuildLineInputs();
        DateTimeOffset? cancellationAt = IsCanceled && CancellationAtLocal.HasValue
            ? AsWallClockUtc(CancellationAtLocal.Value)
            : null;
        DateTimeOffset? ata = !IsCanceled && AtaLocal.HasValue
            ? AsWallClockUtc(AtaLocal.Value)
            : null;
        DateTimeOffset? atd = !IsCanceled && AtdLocal.HasValue
            ? AsWallClockUtc(AtdLocal.Value)
            : null;

        return new CreateWorkOrderForFlightCommand(
            FlightId,
            (FlightNumber ?? string.Empty).Trim().ToUpperInvariant(),
            AircraftTypeId,
            string.IsNullOrWhiteSpace(AircraftTailNumber) ? null : AircraftTailNumber.Trim().ToUpperInvariant(),
            IsCanceled,
            cancellationAt,
            ata,
            atd,
            services,
            tasks,
            Remarks: string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim());
    }

    public UpdateWorkOrderCommand ToUpdateCommand()
    {
        var (services, tasks) = BuildLineInputs();
        DateTimeOffset? cancellationAt = IsCanceled && CancellationAtLocal.HasValue
            ? AsWallClockUtc(CancellationAtLocal.Value)
            : null;
        DateTimeOffset? ata = !IsCanceled && AtaLocal.HasValue
            ? AsWallClockUtc(AtaLocal.Value)
            : null;
        DateTimeOffset? atd = !IsCanceled && AtdLocal.HasValue
            ? AsWallClockUtc(AtdLocal.Value)
            : null;

        return new UpdateWorkOrderCommand(
            WorkOrderId ?? Guid.Empty,
            (FlightNumber ?? string.Empty).Trim().ToUpperInvariant(),
            AircraftTypeId,
            string.IsNullOrWhiteSpace(AircraftTailNumber) ? null : AircraftTailNumber.Trim().ToUpperInvariant(),
            IsCanceled,
            cancellationAt,
            ata,
            atd,
            services,
            tasks,
            Remarks: string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim(),
            ClientMutationId: null);
    }

    // Wall-clock conversion: same convention as the Scheduler — the picker may hand back
    // either Kind=Local or Kind=Unspecified, so we strip the kind and treat the displayed
    // time as a UTC wall-clock to avoid timezone-dependent throws.
    internal static DateTimeOffset AsWallClockUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero);

    private (
        IReadOnlyList<CreateWorkOrderServiceLineInput> Services,
        IReadOnlyList<CreateWorkOrderTaskInput> Tasks) BuildLineInputs()
    {
        var services = ServiceLines
            .Where(l => l.ServiceId.HasValue && l.EmployeeId.HasValue && l.FromLocal.HasValue && l.ToLocal.HasValue)
            .Select(l => new CreateWorkOrderServiceLineInput(
                l.ServiceId!.Value,
                l.EmployeeId!.Value,
                AsWallClockUtc(l.FromLocal!.Value),
                AsWallClockUtc(l.ToLocal!.Value),
                string.IsNullOrWhiteSpace(l.Description) ? null : l.Description.Trim(),
                l.ReturnToRamp))
            .ToList();

        var tasks = Tasks
            .Where(t => t.FromLocal.HasValue && t.ToLocal.HasValue)
            .Select(t => new CreateWorkOrderTaskInput(
                t.TaskType,
                string.IsNullOrWhiteSpace(t.Description) ? null : t.Description.Trim(),
                AsWallClockUtc(t.FromLocal!.Value),
                AsWallClockUtc(t.ToLocal!.Value),
                t.ReturnToRamp,
                t.EmployeeIds.ToList(),
                t.ToolIds.ToList(),
                t.MaterialIds.ToList(),
                t.GeneralSupportIds.ToList(),
                Array.Empty<CreateWorkOrderTaskAttachmentInput>()))
            .ToList();

        return (services, tasks);
    }
}

public sealed class WorkOrderServiceLineRow
{
    public Guid? ServiceId { get; set; }
    public Guid? EmployeeId { get; set; }
    public DateTime? FromLocal { get; set; }
    public DateTime? ToLocal { get; set; }
    public string? Description { get; set; }
    public bool ReturnToRamp { get; set; }

    public bool IsValid() =>
        ServiceId.HasValue && EmployeeId.HasValue && FromLocal.HasValue && ToLocal.HasValue && ToLocal >= FromLocal;
}

/// <summary>
/// Portal-side row for a <see cref="WorkOrderTaskDto"/>. Replaces the legacy
/// <c>WorkOrderEmployeeLineRow</c> + <c>WorkOrderCorrectiveActionRow</c> pair. Attachments
/// are not yet authored in the portal — only the mobile app captures them — so this row
/// only carries the structural fields that the portal review UI needs to display.
/// </summary>
public sealed class WorkOrderTaskRow
{
    public TaskType TaskType { get; set; } = TaskType.Minor;
    public string? Description { get; set; }
    public DateTime? FromLocal { get; set; }
    public DateTime? ToLocal { get; set; }
    public bool ReturnToRamp { get; set; }
    public List<Guid> EmployeeIds { get; set; } = new();
    public List<Guid> ToolIds { get; set; } = new();
    public List<Guid> MaterialIds { get; set; } = new();
    public List<Guid> GeneralSupportIds { get; set; } = new();

    public bool IsValid() =>
        FromLocal.HasValue && ToLocal.HasValue && ToLocal >= FromLocal;
}
