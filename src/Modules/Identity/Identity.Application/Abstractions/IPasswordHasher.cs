namespace Identity.Application.Abstractions;

/// <summary>Hashes and verifies passwords (PBKDF2 via ASP.NET Core's PasswordHasher).</summary>
public interface IPasswordHasher
{
    public string Hash(string password);

    public bool Verify(string passwordHash, string providedPassword);
}
