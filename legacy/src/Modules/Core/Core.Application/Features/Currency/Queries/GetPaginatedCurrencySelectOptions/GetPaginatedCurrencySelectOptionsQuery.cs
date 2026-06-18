using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.Currency;

namespace Core.Application.Features.Currency.Queries.GetPaginatedCurrencySelectOptions;

/// <param name="ContractCurrencyOnly">
/// When <c>true</c>, restricts results to the platform currency itself plus any currency that
/// has an exchange rate set into the platform currency. The contract wizard uses this so the
/// chosen contract currency can always be converted back to the platform currency for billing.
/// </param>
/// <param name="PlatformCurrencyCode">
/// 3-letter ISO platform currency code (e.g. <c>"SAR"</c>) — required when
/// <paramref name="ContractCurrencyOnly"/> is <c>true</c>; ignored otherwise.
/// </param>
public sealed record GetPaginatedCurrencySelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null,
    bool ContractCurrencyOnly = false,
    string? PlatformCurrencyCode = null
) : IQuery<PaginatedResult<CurrencySelectOption>>;
