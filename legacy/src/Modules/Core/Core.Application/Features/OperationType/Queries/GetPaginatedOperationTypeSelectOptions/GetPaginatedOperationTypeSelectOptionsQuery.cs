using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.OperationType;
using Identity.Domain.Authorization;

namespace Core.Application.Features.OperationType.Queries.GetPaginatedOperationTypeSelectOptions;

/// <param name="IncludeAdHoc">
/// Default <c>true</c> to preserve existing dropdown behaviour. Contract creation passes
/// <c>false</c> so the seeded Ad Hoc operation type does not appear in the picker (the domain
/// rejects it anyway).
/// </param>
public sealed record GetPaginatedOperationTypeSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null,
    bool IncludeAdHoc = true
) : IQuery<PaginatedResult<OperationTypeSelectOption>>, IRequirePermission
{
    public string RequiredPermission => Permissions.Scheduler.ReadLookups;
}
