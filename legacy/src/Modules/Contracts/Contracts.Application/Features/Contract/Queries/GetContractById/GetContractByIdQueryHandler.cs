using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Contracts.Application.Features.Contract.Shared;
using Contracts.Contracts.Contract;
using Contracts.Domain.Aggregates.Contract;

namespace Contracts.Application.Features.Contract.Queries.GetContractById;

public sealed class GetContractByIdQueryHandler(IContractRepository contracts)
    : IQueryHandler<GetContractByIdQuery, ContractDto>
{
    public async Task<Result<ContractDto>> Handle(GetContractByIdQuery request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetByIdWithDetailsAsync(ContractId.From(request.Id), cancellationToken);
        if (contract is null) return Error.NotFound("Contract not found.");

        return ContractDtoProjection.ToDto(contract);
    }
}
