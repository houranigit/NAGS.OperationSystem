namespace Identity.Domain.Aggregates.Role;

public sealed class RolePermission
{
    public RoleId RoleId { get; private set; } = null!;
    public string Permission { get; private set; } = null!;

    private RolePermission() { }

    internal static RolePermission Create(RoleId roleId, string permission) =>
        new() { RoleId = roleId, Permission = permission };
}
