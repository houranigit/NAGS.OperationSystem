using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.User;
using Identity.Domain.ValueObjects;

namespace Identity.Domain.Services;

/// <summary>
/// Domain service for password hashing and verification.
/// Keeps BCrypt (infrastructure concern) out of the domain.
/// Inject via handler; pass the resulting PasswordHash into the aggregate (double dispatch).
/// </summary>
public sealed class PasswordService(IPasswordHasher hasher)
{
    public Result<PasswordHash> HashPassword(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            return Error.Validation("Password is required.");

        if (plaintext.Length < 8)
            return Error.Validation("Password must be at least 8 characters.");

        if (plaintext.Length > 256)
            return Error.Validation("Password exceeds maximum length.");

        var hash = hasher.Hash(plaintext);
        return PasswordHash.Create(hash);
    }

    /// <summary>
    /// Validates that the plaintext password is not in the recent history,
    /// then hashes and returns it.
    /// Double dispatch: handler fetches history, service validates against it.
    /// </summary>
    public Result<PasswordHash> ValidateAndHashPassword(
        string plaintext,
        IReadOnlyList<PasswordHistoryEntry> recentHistory)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            return Error.Validation("Password is required.");

        if (plaintext.Length < 8)
            return Error.Validation("Password must be at least 8 characters.");

        if (plaintext.Length > 256)
            return Error.Validation("Password exceeds maximum length.");

        // Check against recent history
        foreach (var entry in recentHistory)
        {
            if (hasher.Verify(plaintext, entry.Hash.Value))
                return Error.Conflict("Password has been used recently. Please choose a different password.");
        }

        var hash = hasher.Hash(plaintext);
        return PasswordHash.Create(hash);
    }

    public bool VerifyPassword(string plaintext, PasswordHash hash) =>
        hasher.Verify(plaintext, hash.Value);
}
