using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Contracts.Features.Role;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Queries.GetAllRoleSelectOptions;

/// <summary>
/// Returns the full role list as cheap select options for the Users / RoleAssignments dialog. The role
/// table is small and rarely changes, so we don't bother paginating here — same approach as
/// <c>GetAllCountrySelectOptionsQuery</c>.
/// </summary>
public sealed class GetAllRoleSelectOptionsQueryHandler(IIdentityDbContext db)
    : IQueryHandler<GetAllRoleSelectOptionsQuery, IReadOnlyList<RoleSelectOption>>
{
    public async Task<Result<IReadOnlyList<RoleSelectOption>>> Handle(
        GetAllRoleSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var roles = await db.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleSelectOption(r.Id.Value, r.Name, r.Description))
            .ToListAsync(cancellationToken);

        return roles;
    }
}
