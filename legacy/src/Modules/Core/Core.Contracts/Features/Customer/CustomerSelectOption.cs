namespace Core.Contracts.Features.Customer;

public sealed record CustomerSelectOption(
    Guid Id,
    string IataCode,
    string Name);
