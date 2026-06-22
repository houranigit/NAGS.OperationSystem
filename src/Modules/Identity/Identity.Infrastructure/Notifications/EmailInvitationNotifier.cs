using System.Net;
using BuildingBlocks.Application.Abstractions;
using Identity.Application;
using Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Notifications;

/// <summary>
/// Delivers invitations as HTML email through the shared <see cref="IEmailSender"/>. When email is
/// disabled the underlying sender logs instead of sending; the activation token is always logged in
/// that case so local/dev activation remains possible without a mail server.
/// </summary>
public sealed class EmailInvitationNotifier(
    IEmailSender emailSender,
    IOptions<IdentityModuleOptions> options,
    ILogger<EmailInvitationNotifier> logger) : IInvitationNotifier
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task SendInvitationAsync(string email, string displayName, Guid userId, Guid invitationToken, CancellationToken cancellationToken = default)
    {
        var link = $"{_options.ActivationUrlBase}?email={WebUtility.UrlEncode(email)}&token={invitationToken}";

        if (!emailSender.IsEnabled)
            logger.LogInformation("Invitation for {Email} (user {UserId}). Activation token: {Token}", email, userId, invitationToken);

        var html =
            $"""
             <p>Hello {WebUtility.HtmlEncode(displayName)},</p>
             <p>You have been invited to the Operations System portal. Use the link below to set your password and activate your account.</p>
             <p><a href="{WebUtility.HtmlEncode(link)}">Activate your account</a></p>
             <p>This invitation expires in {_options.InvitationExpiryHours} hours.</p>
             """;

        await emailSender.SendAsync(
            new EmailMessage(email, displayName, "Activate your Operations System account", html),
            cancellationToken);
    }
}
