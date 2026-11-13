namespace Operations.Application.Contracts;

public sealed record FlightServiceBatchChange(Guid RecordId, SaveFlightServiceRequest? Input);
public sealed record FlightTaskBatchChange(Guid RecordId, SaveFlightTaskRequest? Input);

/// <summary>One reviewed flight edit. A null work input deletes that exact stable record.</summary>
public sealed record SaveFlightOperationsBatchRequest(
    SaveFlightArrivalRequest? Arrival = null,
    SaveFlightDepartureRequest? Departure = null,
    IReadOnlyList<FlightServiceBatchChange>? ServiceChanges = null,
    IReadOnlyList<FlightTaskBatchChange>? TaskChanges = null);
