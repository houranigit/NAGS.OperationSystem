using System.Net;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using Identity.Application;
using Identity.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Notifications;

/// <summary>
/// Queues invitation emails durably: the rendered message is written to the Identity outbox (body
/// encrypted) and delivered with retry by the outbox processor. Nothing is sent synchronously and
/// the raw token is never logged or stored in cleartext.
/// </summary>
public sealed class EmailInvitationNotifier(
    IIdentityDbContext db,
    IEmailContentProtector protector,
    IEmailSender emailSender,
    IHostEnvironment environment,
    ILogger<EmailInvitationNotifier> logger,
    IOptions<IdentityModuleOptions> options) : IInvitationNotifier
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task SendInvitationAsync(string email, string displayName, Guid userId, string invitationToken, CancellationToken cancellationToken = default)
    {
        var link = $"{_options.ActivationUrlBase}?email={WebUtility.UrlEncode(email)}&token={WebUtility.UrlEncode(invitationToken)}";

        var html =
            $"""
             <p>Hello {WebUtility.HtmlEncode(displayName)},</p>
             <p>You have been invited to the Operations System portal. Use the link below to set your password and activate your account.</p>
             <p><a href="{WebUtility.HtmlEncode(link)}">Activate your account</a></p>
             <p>This invitation expires in {_options.InvitationExpiryHours} hours.</p>
             """;

        db.EnqueueEmail(protector, new EmailMessage(email, displayName, "Activate your Operations System account", html), kind: "invitation");
        await db.SaveChangesAsync(cancellationToken);

        if (!emailSender.IsEnabled && environment.IsDevelopment())
        {
            logger.LogWarning(
                "Development invitation token for {Email}: {InvitationToken}. Activation link: {ActivationLink}",
                email, invitationToken, link);
        }
    }
}
