using BuildingBlocks.Application.Abstractions.Commands;
using Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;

namespace Operations.Application.Features.Flight.Commands.CreateFromScratch;

/// <summary>
/// Mobile "Create work order from scratch" command. Creates an ad-hoc Flight (always
/// AdHoc operation type, no contract, station fixed to the caller's station) AND a
/// work order on it in a single transaction. The caller is recorded as the only
/// assigned employee and as the work order's <c>CreatedByEmployeeId</c>.
/// <see cref="CustomerId"/> is required: ad-hoc flights bind to a real customer so
/// retroactive billing can attach them to a contract later.
/// </summary>
/// <param name="ClientMutationId">
/// Optional client-generated idempotency key for the work order. Mirrors
/// <c>CreateWorkOrderForFlightCommand.ClientMutationId</c>: when supplied and an existing
/// work order matches, the handler returns the existing ids without creating new
/// aggregates. Set by the mobile outbox on every retry.
/// </param>
/// <param name="ClientFlightId">
/// Optional client-generated idempotency key for the ad-hoc flight. Lets the mobile
/// client pre-allocate a flight id while offline so the same logical flight cannot be
/// created twice on retry. When supplied and an existing flight matches, the handler
/// short-circuits flight creation and only ensures a work order exists (matched by
/// <see cref="ClientMutationId"/>).
/// </param>
public sealed record CreateAdHocWorkOrderFromScratchCommand(
    Guid CreatorEmployeeId,
    Guid CustomerId,
    string FlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    bool IsCanceled,
    DateTimeOffset? CancellationAt,
    DateTimeOffset? Ata,
    DateTimeOffset? Atd,
    IReadOnlyList<CreateWorkOrderServiceLineInput>? ServiceLines = null,
    IReadOnlyList<CreateWorkOrderTaskInput>? Tasks = null,
    string? Remarks = null,
    byte[]? CustomerSignature = null,
    Guid? ClientMutationId = null,
    Guid? ClientFlightId = null) : ICommand<CreateAdHocFromScratchResult>;

/// <summary>
/// Result payload — the new (or pre-existing) flight + work order ids. <c>Idempotent</c>
/// is <c>true</c> when the request matched a prior submission via
/// <c>ClientFlightId</c> / <c>ClientMutationId</c> and nothing new was created.
/// </summary>
public sealed record CreateAdHocFromScratchResult(Guid FlightId, Guid WorkOrderId, bool Idempotent);
