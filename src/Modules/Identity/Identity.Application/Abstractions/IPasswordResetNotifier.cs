namespace Identity.Application.Abstractions;

/// <summary>Sends the password-reset email. <paramref name="resetToken"/> is the raw (unhashed) token.</summary>
public interface IPasswordResetNotifier
{
    public Task SendPasswordResetAsync(string email, string displayName, Guid userId, string resetToken, CancellationToken cancellationToken = default);
}
