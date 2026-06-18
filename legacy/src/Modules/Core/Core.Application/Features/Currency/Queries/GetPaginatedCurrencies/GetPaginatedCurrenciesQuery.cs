using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Currency;

namespace Core.Application.Features.Currency.Queries.GetPaginatedCurrencies;

public sealed record GetPaginatedCurrenciesQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<CurrencyDto>>;
