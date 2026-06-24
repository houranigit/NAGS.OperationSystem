using System.Net;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using Identity.Application;
using Identity.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Notifications;

/// <summary>Queues linked-email verification messages durably (encrypted body, retried by the outbox).</summary>
public sealed class EmailLinkedEmailVerificationNotifier(
    IIdentityDbContext db,
    IEmailContentProtector protector,
    IOptions<IdentityModuleOptions> options) : ILinkedEmailVerificationNotifier
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task SendVerificationAsync(string newEmail, string displayName, Guid userId, string verificationToken, CancellationToken cancellationToken = default)
    {
        var link = $"{_options.EmailChangeConfirmUrlBase}?email={WebUtility.UrlEncode(newEmail)}&token={WebUtility.UrlEncode(verificationToken)}";

        var html =
            $"""
             <p>Hello {WebUtility.HtmlEncode(displayName)},</p>
             <p>Please confirm this email address so it can become your Operations System sign-in email. Until you confirm, your current login email stays in effect.</p>
             <p><a href="{WebUtility.HtmlEncode(link)}">Confirm this email address</a></p>
             <p>This link expires in {_options.EmailChangeExpiryHours} hours.</p>
             """;

        db.EnqueueEmail(protector, new EmailMessage(newEmail, displayName, "Confirm your Operations System email", html), kind: "email-verification");
        await db.SaveChangesAsync(cancellationToken);
    }
}
