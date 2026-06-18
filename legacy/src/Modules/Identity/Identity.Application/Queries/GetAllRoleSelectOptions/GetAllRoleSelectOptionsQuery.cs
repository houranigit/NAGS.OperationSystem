using BuildingBlocks.Application.Abstractions.Queries;
using Identity.Contracts.Features.Role;

namespace Identity.Application.Queries.GetAllRoleSelectOptions;

public sealed record GetAllRoleSelectOptionsQuery : IQuery<IReadOnlyList<RoleSelectOption>>;
