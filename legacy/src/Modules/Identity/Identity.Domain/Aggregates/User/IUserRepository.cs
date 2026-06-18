namespace Identity.Domain.Aggregates.User;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByEmailOrUsernameAsync(string emailOrUsername, CancellationToken ct = default);
    Task<User?> GetByInvitationTokenAsync(string token, CancellationToken ct = default);
    Task<User?> GetByIdWithRolesAsync(UserId id, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetAllWithRolesAsync(CancellationToken ct = default);
    void Add(User user);
    void Update(User user);
    void Remove(User user);
}
