namespace Identity.Application.Abstractions;

/// <summary>
/// Sends the linked-email reverification message to the pending new address. The login email only
/// changes after the recipient confirms via this link, so an undeliverable address cannot lock the
/// account out. <paramref name="verificationToken"/> is the raw (unhashed) token.
/// </summary>
public interface ILinkedEmailVerificationNotifier
{
    public Task SendVerificationAsync(string newEmail, string displayName, Guid userId, string verificationToken, CancellationToken cancellationToken = default);
}
