using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Service;

namespace Core.Application.Features.Service.Queries.GetPaginatedServiceSelectOptions;

public sealed record GetPaginatedServiceSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ServiceSelectOption>>;
