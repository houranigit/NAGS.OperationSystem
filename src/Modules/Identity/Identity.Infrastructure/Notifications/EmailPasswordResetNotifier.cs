using System.Net;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using Identity.Application;
using Identity.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Notifications;

/// <summary>Queues password-reset emails durably (encrypted body, retried by the outbox).</summary>
public sealed class EmailPasswordResetNotifier(
    IIdentityDbContext db,
    IEmailContentProtector protector,
    IOptions<IdentityModuleOptions> options) : IPasswordResetNotifier
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task SendPasswordResetAsync(string email, string displayName, Guid userId, string resetToken, CancellationToken cancellationToken = default)
    {
        var link = $"{_options.PasswordResetUrlBase}?email={WebUtility.UrlEncode(email)}&token={WebUtility.UrlEncode(resetToken)}";

        var html =
            $"""
             <p>Hello {WebUtility.HtmlEncode(displayName)},</p>
             <p>We received a request to reset your Operations System password. Use the link below to choose a new password.</p>
             <p><a href="{WebUtility.HtmlEncode(link)}">Reset your password</a></p>
             <p>This link expires in {_options.PasswordResetExpiryHours} hours. If you did not request this, you can ignore this email.</p>
             """;

        db.EnqueueEmail(protector, new EmailMessage(email, displayName, "Reset your Operations System password", html), kind: "password-reset");
        await db.SaveChangesAsync(cancellationToken);
    }
}
