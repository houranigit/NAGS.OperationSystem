using Identity.Domain.Aggregates.User;
using Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    public async Task<User?> GetByIdAsync(UserId id, CancellationToken ct = default) =>
        await context.Users.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var parsed = Email.Create(email);
        if (parsed.IsFailure)
            return null;

        var emailValue = parsed.Value.Value;
        return await context.Users.FirstOrDefaultAsync(x => x.Email.Value == emailValue, ct);
    }

    public async Task<User?> GetByEmailOrUsernameAsync(string emailOrUsername, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailOrUsername))
            return null;

        var trimmed = emailOrUsername.Trim();
        var emailParsed = Email.Create(trimmed);
        var usernameParsed = Username.Create(trimmed);

        if (emailParsed.IsSuccess && usernameParsed.IsSuccess)
        {
            var emailValue = emailParsed.Value.Value;
            var usernameValue = usernameParsed.Value.Value;
            return await context.Users.FirstOrDefaultAsync(
                x => x.Email.Value == emailValue || x.Username.Value == usernameValue,
                ct);
        }

        if (emailParsed.IsSuccess)
        {
            var emailValue = emailParsed.Value.Value;
            return await context.Users.FirstOrDefaultAsync(x => x.Email.Value == emailValue, ct);
        }

        if (usernameParsed.IsSuccess)
        {
            var usernameValue = usernameParsed.Value.Value;
            return await context.Users.FirstOrDefaultAsync(x => x.Username.Value == usernameValue, ct);
        }

        return null;
    }

    public async Task<User?> GetByInvitationTokenAsync(string token, CancellationToken ct = default) =>
        await context.Users.FirstOrDefaultAsync(x => x.InvitationToken == token, ct);

    public async Task<User?> GetByIdWithRolesAsync(UserId id, CancellationToken ct = default) =>
        await context.Users.Include("Roles").FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) =>
        await context.Users.ToListAsync(ct);

    public async Task<IReadOnlyList<User>> GetAllWithRolesAsync(CancellationToken ct = default) =>
        await context.Users.AsSplitQuery().Include("Roles").ToListAsync(ct);

    public void Add(User user) => context.Users.Add(user);
    public void Update(User user) => context.Users.Update(user);
    public void Remove(User user) => context.Users.Remove(user);
}
