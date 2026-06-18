using BuildingBlocks.Application.Abstractions.Commands;

namespace Contracts.Application.Features.Contract.Commands.TerminateContract;

/// <summary>
/// Manual hard exit. Refused when the contract is already <c>Terminated</c> or <c>Expired</c>.
/// Captures who, when, and why.
/// </summary>
public sealed record TerminateContractCommand(Guid Id, string Reason) : ICommand;
