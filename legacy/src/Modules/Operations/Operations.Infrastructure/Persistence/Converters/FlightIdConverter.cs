using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Infrastructure.Persistence;

public sealed class FlightIdConverter() : ValueConverter<FlightId, Guid>(
    id => id.Value,
    value => FlightId.From(value));
