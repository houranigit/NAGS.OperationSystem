using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Events;

namespace Identity.Domain.Aggregates.Role;

public sealed class Role : AggregateRoot<RoleId>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsSystemRole { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private readonly List<RolePermission> _permissions = [];
    public IReadOnlyList<RolePermission> Permissions => _permissions.AsReadOnly();

    private Role() { }

    public static Result<Role> Create(string name, string? description, bool isSystemRole = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Role name is required.");

        if (name.Length > 100)
            return Error.Validation("Role name must not exceed 100 characters.");

        var role = new Role
        {
            Id = RoleId.New(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsSystemRole = isSystemRole,
            CreatedAt = DateTime.UtcNow
        };

        role.RaiseDomainEvent(new RoleCreatedEvent(role.Id));
        return role;
    }

    public Result AddPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return Error.Validation("Permission cannot be empty.");

        if (_permissions.Any(p => p.Permission == permission))
            return Error.Conflict($"Permission '{permission}' already exists on this role.");

        _permissions.Add(RolePermission.Create(Id, permission));
        return Result.Success();
    }

    public Result RemovePermission(string permission)
    {
        var existing = _permissions.FirstOrDefault(p => p.Permission == permission);
        if (existing is null)
            return Error.NotFound($"Permission '{permission}' not found on this role.");

        _permissions.Remove(existing);
        return Result.Success();
    }

    public void ClearPermissions() => _permissions.Clear();

    public IReadOnlyList<string> GetPermissionCodes() =>
        _permissions.Select(p => p.Permission).ToList().AsReadOnly();
}
