using BuildingBlocks.Application.Abstractions.Queries;

namespace Identity.Application.Queries.GetRolesOverview;

public sealed record GetRolesOverviewQuery : IQuery<RolesOverviewDto>;

public sealed record RolesOverviewDto(
    IReadOnlyList<RoleCardDto> Roles,
    IReadOnlyList<UserRoleRowDto> Users);

public sealed record RoleCardDto(
    Guid Id,
    string Name,
    string? Description,
    int TotalUsers,
    IReadOnlyList<string> SampleDisplayNames,
    IReadOnlyList<string> PermissionCodes);

public sealed record UserRoleRowDto(
    Guid UserId,
    string Username,
    string Email,
    string UserType,
    string Status,
    IReadOnlyList<string> RoleNames);
