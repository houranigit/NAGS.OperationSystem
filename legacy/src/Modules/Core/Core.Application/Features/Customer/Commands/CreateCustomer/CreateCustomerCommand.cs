using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.Customer;

namespace Core.Application.Features.Customer.Commands.CreateCustomer;

/// <param name="Contacts">
/// Full desired contact list each submit — reconciled via aggregate <c>SyncContacts</c> on <see cref="Core.Domain.Aggregates.Customer.Customer"/>:
/// persisted ids updated, ids omitted removed, null ids inserted (same semantics as update commands).
/// </param>
public sealed record CreateCustomerCommand(
    string IataCode,
    string? IcaoCode,
    string Name,
    string? OfficialEmail,
    string? OfficialPhone,
    CustomerAddressInput? Address,
    bool IsActive,
    byte[]? LogoBytes,
    IReadOnlyList<CustomerContactInput> Contacts) : ICommand<Guid>;
