using BuildingBlocks.Application.Abstractions.Queries;
using Contracts.Contracts.Contract;

namespace Contracts.Application.Features.Contract.Queries.GetContractById;

public sealed record GetContractByIdQuery(Guid Id) : IQuery<ContractDto>;
