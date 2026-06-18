namespace Core.Contracts.Features.Customer;

public sealed record CustomerSnapshot(
    Guid CustomerId,
    string IataCode,
    string Name);
