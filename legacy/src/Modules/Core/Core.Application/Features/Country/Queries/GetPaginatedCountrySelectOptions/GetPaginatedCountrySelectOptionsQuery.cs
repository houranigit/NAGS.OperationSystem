using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Country;

namespace Core.Application.Features.Country.Queries.GetPaginatedCountrySelectOptions;

public sealed record GetPaginatedCountrySelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<CountrySelectOption>>;
