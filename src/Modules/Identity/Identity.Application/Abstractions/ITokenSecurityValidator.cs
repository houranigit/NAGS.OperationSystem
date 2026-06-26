namespace Identity.Application.Abstractions;

/// <summary>
/// Validates a presented access token against live state on every request: the user must still be
/// active, the token's security stamp must match the user's current stamp, and the backing session
/// must still be active. Implementations may cache positive validation briefly so remote database
/// latency is not paid on every API request.
/// </summary>
public interface ITokenSecurityValidator
{
    public Task<bool> IsCurrentAsync(
        Guid userId,
        string? securityStamp,
        Guid? sessionId,
        CancellationToken cancellationToken = default);
}
