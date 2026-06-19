using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Authorization;
using Identity.Domain.Roles.Events;

namespace Identity.Domain.Roles;

/// <summary>
/// A named collection of permissions. Authorization is permission-based; roles are how
/// permissions are grouped and assigned to users. System roles are seeded and protected
/// from deletion.
/// </summary>
public sealed class Role : AggregateRoot<Guid>
{
    private readonly List<string> _permissions = [];

    private Role() { }

    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public IReadOnlyCollection<string> Permissions => _permissions;

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static Result<Role> Create(string? name, string? description, IEnumerable<string> permissions, DateTimeOffset now, bool isSystem = false)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var permissionList = permissions?.ToList() ?? [];
        var permissionCheck = ValidatePermissions(permissionList);
        if (permissionCheck.IsFailure)
            return permissionCheck.Error;

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = nameCheck.Value,
            NormalizedName = Normalize(nameCheck.Value),
            Description = NormalizeDescription(description),
            IsSystem = isSystem,
            CreatedAtUtc = now
        };

        foreach (var permission in permissionList.Distinct())
            role._permissions.Add(permission);

        role.RaiseDomainEvent(new RoleCreatedEvent(role.Id, role.Name));
        return role;
    }

    public Result Update(string? name, string? description, DateTimeOffset now)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        Name = nameCheck.Value;
        NormalizedName = Normalize(nameCheck.Value);
        Description = NormalizeDescription(description);
        UpdatedAtUtc = now;
        RaiseDomainEvent(new RoleUpdatedEvent(Id));
        return Result.Success();
    }

    public Result SetPermissions(IEnumerable<string> permissions, DateTimeOffset now)
    {
        var permissionList = permissions?.ToList() ?? [];
        var check = ValidatePermissions(permissionList);
        if (check.IsFailure)
            return check.Error;

        _permissions.Clear();
        foreach (var permission in permissionList.Distinct())
            _permissions.Add(permission);

        UpdatedAtUtc = now;
        RaiseDomainEvent(new RolePermissionsChangedEvent(Id, _permissions.ToList()));
        return Result.Success();
    }

    public bool HasPermission(string permission) => _permissions.Contains(permission);

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Role name is required.", "Identity.Role.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
            return Error.Validation("Role name must not exceed 100 characters.", "Identity.Role.NameTooLong");

        return trimmed;
    }

    private static Result ValidatePermissions(IReadOnlyList<string> permissions)
    {
        foreach (var permission in permissions)
        {
            if (!IdentityPermissions.IsKnown(permission))
                return Error.Validation($"Unknown permission '{permission}'.", "Identity.Role.UnknownPermission");
        }

        return Result.Success();
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
