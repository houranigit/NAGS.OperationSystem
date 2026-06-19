using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Security;

/// <summary>PBKDF2 password hashing backed by ASP.NET Core's <see cref="PasswordHasher{TUser}"/>.</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private static readonly object Placeholder = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(Placeholder, password);

    public bool Verify(string passwordHash, string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(Placeholder, passwordHash, providedPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
