using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Contracts.Domain.Aggregates.Contract;

namespace Contracts.Application.Features.Contract.Commands.ActivateContract;

public sealed class ActivateContractCommandHandler(
    IContractRepository contracts,
    ICurrentUserService currentUser,
    TimeProvider time)
    : ICommandHandler<ActivateContractCommand>
{
    public async Task<Result> Handle(ActivateContractCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Error.Unauthorized("Authenticated user is required to activate a contract.");

        var contract = await contracts.GetByIdAsync(ContractId.From(request.Id), cancellationToken);
        if (contract is null) return Error.NotFound("Contract not found.");

        var result = contract.Activate(userId.Value, time.GetUtcNow());
        if (result.IsFailure) return result.Error;

        contracts.Update(contract);
        return Result.Success();
    }
}
