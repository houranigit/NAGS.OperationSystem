using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Identity.Contracts.Features.User;

namespace Identity.Application.Queries.GetPaginatedUsers;

public sealed record GetPaginatedUsersQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<UserDto>>;
