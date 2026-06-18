using BuildingBlocks.Application.Abstractions.Commands;

namespace Contracts.Application.Features.Contract.Commands.SuspendContract;

/// <summary>Manual suspension. Allowed only when the contract is currently <c>Active</c>.</summary>
public sealed record SuspendContractCommand(Guid Id, string Reason) : ICommand;
