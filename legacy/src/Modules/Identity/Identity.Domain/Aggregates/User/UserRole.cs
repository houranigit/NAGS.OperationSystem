using Identity.Domain.Aggregates.Role;

namespace Identity.Domain.Aggregates.User;

public sealed class UserRole
{
    public UserId UserId { get; private set; } = null!;
    public RoleId RoleId { get; private set; } = null!;
    public DateTime AssignedAt { get; private set; }

    private UserRole() { }

    internal static UserRole Create(UserId userId, RoleId roleId) =>
        new()
        {
            UserId = userId,
            RoleId = roleId,
            AssignedAt = DateTime.UtcNow
        };
}
