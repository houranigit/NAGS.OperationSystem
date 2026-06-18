using BuildingBlocks.Application.Abstractions.Commands;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;

/// <summary>
/// Creates a new work order linked to an existing flight. The flight supplies the shared
/// snapshot data (customer, station, operation type, schedule); the user only enters fields
/// that may legitimately differ on the work order — flight number, aircraft type, tail
/// number, actual times, cancellation flag, plus optional service lines and tasks. Both
/// line collections are optional — a work order may be created with none of them (e.g. a
/// cancel-flight work order carries only the cancellation timestamp).
/// </summary>
/// <param name="ClientMutationId">
/// Optional client-generated idempotency key. When supplied, the handler short-circuits if
/// a work order with the same key already exists (returns the existing id with
/// <see cref="CreateWorkOrderForFlightResult.Idempotent"/> = <c>true</c>). Used by the
/// mobile outbox to safely retry after ambiguous-timeout failures (server received the
/// request but the response was dropped). <c>null</c> for portal-originated and server-job
/// submissions where no client-side retry pipeline exists.
/// </param>
public sealed record CreateWorkOrderForFlightCommand(
    Guid FlightId,
    string FlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    bool IsCanceled,
    DateTimeOffset? CancellationAt,
    DateTimeOffset? Ata,
    DateTimeOffset? Atd,
    IReadOnlyList<CreateWorkOrderServiceLineInput>? ServiceLines = null,
    IReadOnlyList<CreateWorkOrderTaskInput>? Tasks = null,
    string? Remarks = null,
    Guid? CreatedByEmployeeId = null,
    byte[]? CustomerSignature = null,
    Guid? ClientMutationId = null) : ICommand<CreateWorkOrderForFlightResult>;

/// <summary>
/// Result of <see cref="CreateWorkOrderForFlightCommand"/>. <see cref="WorkOrderId"/> is
/// either the freshly-created work order or the existing one matched by
/// <see cref="CreateWorkOrderForFlightCommand.ClientMutationId"/>. <see cref="FlightId"/>
/// echoes the flight the work order is attached to so the mobile outbox can resolve the
/// optimistic chip without a second round-trip. <see cref="Idempotent"/> is <c>true</c>
/// only when a prior submission with the same mutation id was found (in that case the
/// handler does NOT create another aggregate).
/// </summary>
public sealed record CreateWorkOrderForFlightResult(
    Guid WorkOrderId,
    Guid FlightId,
    bool Idempotent);

/// <summary>UI-side service line. Service + employee snapshots are resolved at handler time.</summary>
public sealed record CreateWorkOrderServiceLineInput(
    Guid ServiceId,
    Guid EmployeeId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Description,
    bool ReturnToRamp);

/// <summary>
/// UI-side task input — replaces the legacy employee-line + corrective-action shapes.
/// Carries severity, period, participating employees, optional store usage, attachments,
/// and the RTR flag. All snapshots are resolved at handler time.
/// </summary>
public sealed record CreateWorkOrderTaskInput(
    TaskType TaskType,
    string? Description,
    DateTimeOffset From,
    DateTimeOffset To,
    bool ReturnToRamp,
    IReadOnlyList<Guid> EmployeeIds,
    IReadOnlyList<Guid> ToolIds,
    IReadOnlyList<Guid> MaterialIds,
    IReadOnlyList<Guid> GeneralSupportIds,
    IReadOnlyList<CreateWorkOrderTaskAttachmentInput> Attachments);

/// <summary>UI-side task attachment payload. Bytes are sent inline (byte[] storage strategy).</summary>
public sealed record CreateWorkOrderTaskAttachmentInput(
    TaskAttachmentKind Kind,
    string ContentType,
    string FileName,
    byte[] Bytes,
    DateTimeOffset CapturedAt);
