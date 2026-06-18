namespace Identity.Domain.Aggregates.User;

public interface IPasswordHistoryRepository
{
    Task<IReadOnlyList<PasswordHistoryEntry>> GetLastNAsync(UserId userId, int count, CancellationToken ct = default);
    void Add(PasswordHistoryEntry entry);
}
