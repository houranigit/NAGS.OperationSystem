namespace Operations.Domain.Aggregates.Flight;

public interface IFlightRepository
{
    Task<Flight?> GetByIdAsync(FlightId id, CancellationToken cancellationToken = default);
    void Add(Flight flight);
    void Update(Flight flight);
}
