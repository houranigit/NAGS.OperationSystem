using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.License;

namespace Core.Application.Features.License.Queries.GetPaginatedLicenseSelectOptions;

public sealed record GetPaginatedLicenseSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<LicenseSelectOption>>;
