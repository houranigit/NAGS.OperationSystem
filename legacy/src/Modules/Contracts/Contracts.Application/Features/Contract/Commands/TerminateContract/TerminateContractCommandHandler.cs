using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Contracts.Domain.Aggregates.Contract;

namespace Contracts.Application.Features.Contract.Commands.TerminateContract;

public sealed class TerminateContractCommandHandler(
    IContractRepository contracts,
    ICurrentUserService currentUser,
    TimeProvider time)
    : ICommandHandler<TerminateContractCommand>
{
    public async Task<Result> Handle(TerminateContractCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Error.Unauthorized("Authenticated user is required to terminate a contract.");

        var contract = await contracts.GetByIdAsync(ContractId.From(request.Id), cancellationToken);
        if (contract is null) return Error.NotFound("Contract not found.");

        var result = contract.Terminate(request.Reason, userId.Value, time.GetUtcNow());
        if (result.IsFailure) return result.Error;

        contracts.Update(contract);
        return Result.Success();
    }
}
