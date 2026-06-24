namespace Identity.Application.Abstractions;

/// <summary>
/// Sends the account invitation (e.g. email with an activation link). v1.0.0 ships a logging
/// implementation; a real SMTP/email-provider implementation can be swapped in without
/// touching the invite handlers.
/// </summary>
public interface IInvitationNotifier
{
    /// <summary><paramref name="invitationToken"/> is the raw (unhashed) URL-safe token for the activation link.</summary>
    public Task SendInvitationAsync(string email, string displayName, Guid userId, string invitationToken, CancellationToken cancellationToken = default);
}
