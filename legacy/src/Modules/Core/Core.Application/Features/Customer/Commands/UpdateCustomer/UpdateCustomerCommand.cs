using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.Customer;

namespace Core.Application.Features.Customer.Commands.UpdateCustomer;

/// <param name="Contacts">
/// Full snapshot of contacts after edit — aggregate compares ids vs persisted rows (updates matched ids, removes omitted ids, adds null ids).
/// </param>
public sealed record UpdateCustomerCommand(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string? OfficialEmail,
    string? OfficialPhone,
    CustomerAddressInput? Address,
    bool IsActive,
    byte[]? LogoBytes,
    IReadOnlyList<CustomerContactInput> Contacts) : ICommand;
