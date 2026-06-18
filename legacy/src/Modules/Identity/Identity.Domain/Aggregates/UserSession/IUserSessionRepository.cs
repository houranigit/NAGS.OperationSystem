namespace Identity.Domain.Aggregates.UserSession;

public interface IUserSessionRepository
{
    Task<UserSession?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task<IReadOnlyList<UserSession>> GetActiveByUserIdAsync(User.UserId userId, CancellationToken ct = default);
    Task<UserSession?> GetByIdAsync(UserSessionId id, CancellationToken ct = default);
    void Add(UserSession session);
    void Update(UserSession session);
}
