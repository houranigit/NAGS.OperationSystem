using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.AircraftType;

namespace Core.Application.Features.AircraftType.Queries.GetPaginatedAircraftTypes;

public sealed record GetPaginatedAircraftTypesQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<AircraftTypeDto>>;
