using BuildingBlocks.Application.Abstractions.Queries;
using Store.Contracts.Features.Tool;

namespace Store.Application.Features.Tool.Queries.GetToolDetails;

public sealed record GetToolDetailsQuery(Guid Id) : IQuery<ToolDetailsDto>;
