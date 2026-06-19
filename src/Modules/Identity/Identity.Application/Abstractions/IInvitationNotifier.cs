namespace Identity.Application.Abstractions;

/// <summary>
/// Sends the account invitation (e.g. email with an activation link). v1.0.0 ships a logging
/// implementation; a real SMTP/email-provider implementation can be swapped in without
/// touching the invite handlers.
/// </summary>
public interface IInvitationNotifier
{
    public Task SendInvitationAsync(string email, string displayName, Guid userId, Guid invitationToken, CancellationToken cancellationToken = default);
}
