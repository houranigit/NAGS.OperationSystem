using BuildingBlocks.Application.Abstractions.Commands;

namespace Contracts.Application.Features.Contract.Commands.ActivateContract;

/// <summary>
/// Resumes a suspended contract. The new status (Draft / Active / Expired) is recomputed
/// from the contract period at the moment the command runs.
/// </summary>
public sealed record ActivateContractCommand(Guid Id) : ICommand;
