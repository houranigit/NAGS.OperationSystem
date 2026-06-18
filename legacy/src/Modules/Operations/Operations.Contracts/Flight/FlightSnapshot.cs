using Operations.Domain.Enumerations;

namespace Operations.Contracts.Flight;

public sealed record FlightSnapshot(
    Guid FlightId,
    string FlightNumber);
