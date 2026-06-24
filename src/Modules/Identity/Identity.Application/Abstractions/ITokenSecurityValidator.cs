namespace Identity.Application.Abstractions;

/// <summary>
/// Validates a presented access token against live state on every request: the user must still be
/// active, the token's security stamp must match the user's current stamp, and the backing session
/// must still be active. This bounds the lifetime of a token after a password, role, permission,
/// suspension, or MFA change to the time it takes the next request to arrive.
/// </summary>
public interface ITokenSecurityValidator
{
    public Task<bool> IsCurrentAsync(
        Guid userId,
        string? securityStamp,
        Guid? sessionId,
        CancellationToken cancellationToken = default);
}
