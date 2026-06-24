namespace BuildingBlocks.Application.Email;

/// <summary>
/// Encrypts/decrypts email bodies so secret-bearing content (activation, reset, verification links)
/// is never stored in cleartext in the outbox. Backed by ASP.NET Core Data Protection.
/// </summary>
public interface IEmailContentProtector
{
    public string Protect(string plaintext);

    public string Unprotect(string protectedValue);
}
