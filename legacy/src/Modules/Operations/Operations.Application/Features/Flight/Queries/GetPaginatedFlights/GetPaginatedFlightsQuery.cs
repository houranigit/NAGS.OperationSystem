using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Operations.Contracts.Flight;

namespace Operations.Application.Features.Flight.Queries.GetPaginatedFlights;

public sealed record GetPaginatedFlightsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<FlightDto>>;
