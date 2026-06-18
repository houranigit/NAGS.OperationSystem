using BuildingBlocks.Application.Abstractions.Commands;
using Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;

namespace Operations.Application.Features.WorkOrder.Commands.UpdateWorkOrder;

/// <summary>
/// Edits an existing <c>UnderReview</c> work order. The flight-locked snapshot fields
/// (customer / station / operation type / STA / STD) are not editable — they are kept in
/// sync with the linked flight. The user may change flight number, aircraft type, tail,
/// ATA/ATD, IsCanceled flag, and replace all line collections. Service lines and tasks
/// reuse the create-WO input shapes so the dialog form model can flow into either command
/// without translating shapes.
/// </summary>
public sealed record UpdateWorkOrderCommand(
    Guid WorkOrderId,
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
    byte[]? CustomerSignature = null,
    /// <summary>
    /// Optional mobile outbox correlation id — echoed as <c>OriginMutationId</c> on flight
    /// sync envelopes after update so the device can drop its queued row when the push arrives.
    /// </summary>
    Guid? ClientMutationId = null) : ICommand;
