using BuildingBlocks.Application.Abstractions.Commands;
using Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;

namespace Operations.Application.Features.WorkOrder.Commands.RecordReturnToRampLines;

/// <summary>
/// Append-only mutation used by the mobile "return to ramp" flow. The supplied service
/// lines and tasks are <b>added</b> to the work order with <c>ReturnToRamp = true</c>
/// regardless of what the input rows say — the handler forces the flag and the aggregate
/// forces it again as a defence-in-depth. Existing rows are preserved, never replaced.
/// Only valid while the work order is <c>UnderReview</c>.
/// </summary>
public sealed record RecordReturnToRampLinesCommand(
    Guid WorkOrderId,
    IReadOnlyList<CreateWorkOrderServiceLineInput>? ServiceLines = null,
    IReadOnlyList<CreateWorkOrderTaskInput>? Tasks = null,
    byte[]? CustomerSignature = null,
    /// <summary>Optional mobile outbox id — echoed as <c>OriginMutationId</c> on flight sync pushes.</summary>
    Guid? ClientMutationId = null) : ICommand;
