using Identity.Domain.ValueObjects;

namespace Identity.Domain.Aggregates.User;

/// <summary>
/// Tracks password history so users can't reuse recent passwords.
/// Not an aggregate root — plain entity class.
/// </summary>
public sealed class PasswordHistoryEntry
{
    public Guid Id { get; private set; }
    public UserId UserId { get; private set; } = null!;
    public PasswordHash Hash { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    private PasswordHistoryEntry() { }

    public static PasswordHistoryEntry Create(UserId userId, PasswordHash hash) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Hash = hash,
            CreatedAt = DateTime.UtcNow
        };
}
