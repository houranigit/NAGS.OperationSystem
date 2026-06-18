using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.Material;

namespace Store.Application.Features.Material.Queries.GetPaginatedMaterialSelectOptions;

public sealed record GetPaginatedMaterialSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<MaterialSelectOption>>;
