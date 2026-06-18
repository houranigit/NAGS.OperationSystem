using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;

namespace Identity.Application.Queries.GetRolesOverview;

public sealed class GetRolesOverviewQueryHandler(
    IRoleRepository roleRepository,
    IUserRepository userRepository)
    : IQueryHandler<GetRolesOverviewQuery, RolesOverviewDto>
{
    public async Task<Result<RolesOverviewDto>> Handle(
        GetRolesOverviewQuery request,
        CancellationToken cancellationToken)
    {
        var roles = await roleRepository.GetAllAsync(cancellationToken);
        var users = await userRepository.GetAllWithRolesAsync(cancellationToken);

        var roleById = roles.ToDictionary(r => r.Id);

        var roleCards = roles
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(role =>
            {
                var inRole = users
                    .Where(u => u.Roles.Any(ur => ur.RoleId == role.Id))
                    .OrderBy(u => u.Username.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var samples = inRole
                    .Take(4)
                    .Select(u => u.Username.Value)
                    .ToList();

                return new RoleCardDto(
                    role.Id.Value,
                    role.Name,
                    role.Description,
                    inRole.Count,
                    samples,
                    role.GetPermissionCodes());
            })
            .ToList();

        var userRows = users
            .OrderBy(u => u.Username.Value, StringComparer.OrdinalIgnoreCase)
            .Select(u =>
            {
                var names = u.Roles
                    .Select(ur => roleById.TryGetValue(ur.RoleId, out var r) ? r.Name : ur.RoleId.Value.ToString())
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new UserRoleRowDto(
                    u.Id.Value,
                    u.Username.Value,
                    u.Email.Value,
                    u.UserType.Name,
                    u.Status.Name,
                    names);
            })
            .ToList();

        return new RolesOverviewDto(roleCards, userRows);
    }
}
