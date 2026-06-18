using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.OperationType;

namespace Core.Application.Features.OperationType.Queries.GetPaginatedOperationTypes;

public sealed record GetPaginatedOperationTypesQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<OperationTypeDto>>;
