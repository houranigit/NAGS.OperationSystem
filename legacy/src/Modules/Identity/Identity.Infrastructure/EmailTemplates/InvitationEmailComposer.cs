using System.Net;
using BuildingBlocks.Application.Abstractions;
using Identity.Application.EmailTemplates;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.EmailTemplates;

/// <summary>
/// Default invitation email — branded with the platform's <c>AppName</c> and pointing at
/// <c>PortalBaseUrl/account/activate</c>. The HTML is intentionally inline-styled so it survives
/// strict mail clients (Outlook on Office365 strips most external CSS).
/// </summary>
public sealed class InvitationEmailComposer(IOptionsMonitor<InvitationEmailSettings> settings)
    : IInvitationEmailComposer
{
    public EmailMessage BuildInvitation(
        string recipientEmail,
        string recipientDisplayName,
        string invitationToken,
        DateTime expiresAtUtc)
    {
        var s = settings.CurrentValue;
        var appName = string.IsNullOrWhiteSpace(s.AppName) ? "Operations" : s.AppName;
        var portalBase = (s.PortalBaseUrl ?? string.Empty).TrimEnd('/');

        var activationUrl = string.IsNullOrEmpty(portalBase)
            ? $"/account/activate?email={WebUtility.UrlEncode(recipientEmail)}&token={WebUtility.UrlEncode(invitationToken)}"
            : $"{portalBase}/account/activate?email={WebUtility.UrlEncode(recipientEmail)}&token={WebUtility.UrlEncode(invitationToken)}";

        var displayName = string.IsNullOrWhiteSpace(recipientDisplayName)
            ? recipientEmail
            : recipientDisplayName.Trim();

        var subject = $"You're invited to {appName}";

        var plain =
            $"Hello {displayName},\n\n" +
            $"You've been invited to {appName}. Please activate your account using the link below:\n\n" +
            $"{activationUrl}\n\n" +
            $"This invitation expires on {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.\n\n" +
            $"If you weren't expecting this email you can safely ignore it.\n\n" +
            $"— {appName}";

        var html =
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <title>{WebUtility.HtmlEncode(subject)}</title>
            </head>
            <body style="margin:0;padding:0;background:#f5f7fb;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;color:#1f2937;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f5f7fb;padding:32px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="max-width:560px;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 8px 32px rgba(15,23,42,0.06);">
                      <tr>
                        <td style="background:linear-gradient(135deg,#722f37 0%,#9a3240 100%);padding:24px 32px;color:#fff;font-size:18px;font-weight:700;letter-spacing:0.3px;">
                          {WebUtility.HtmlEncode(appName)}
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <h1 style="margin:0 0 12px;font-size:22px;font-weight:700;color:#0f172a;">Welcome aboard, {WebUtility.HtmlEncode(displayName)}.</h1>
                          <p style="margin:0 0 20px;font-size:14px;line-height:1.65;color:#475569;">
                            An administrator has invited you to {WebUtility.HtmlEncode(appName)}. Activate your account to set a password and start using the platform.
                          </p>
                          <p style="margin:0 0 28px;text-align:center;">
                            <a href="{activationUrl}"
                               style="display:inline-block;padding:12px 28px;border-radius:999px;background:linear-gradient(145deg,#722f37 0%,#9a3240 52%,#722f37 100%);color:#fff;text-decoration:none;font-weight:600;font-size:14px;">
                              Activate your account
                            </a>
                          </p>
                          <p style="margin:0 0 8px;font-size:12px;color:#94a3b8;">If the button doesn't work, copy and paste this URL into your browser:</p>
                          <p style="margin:0 0 24px;font-size:12px;word-break:break-all;color:#1e293b;">{WebUtility.HtmlEncode(activationUrl)}</p>
                          <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0;" />
                          <p style="margin:0;font-size:12px;color:#94a3b8;">
                            This invitation expires on <strong>{expiresAtUtc:yyyy-MM-dd HH:mm} UTC</strong>.
                            <br />
                            If you weren't expecting this email you can safely ignore it.
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="background:#f8fafc;padding:16px 32px;font-size:11px;color:#94a3b8;">
                          You're receiving this because someone created an account for {WebUtility.HtmlEncode(recipientEmail)}.
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new EmailMessage(
            ToAddress: recipientEmail,
            Subject: subject,
            HtmlBody: html,
            ToDisplayName: displayName,
            PlainTextBody: plain);
    }
}

/// <summary>
/// Branding inputs for invitation emails. Bound to the <c>PlatformSettings</c> section so we don't
/// duplicate the host config — callers pass in <c>AppName</c> and <c>PortalBaseUrl</c>.
/// </summary>
public sealed class InvitationEmailSettings
{
    public string AppName { get; set; } = "Operations";
    public string PortalBaseUrl { get; set; } = "";
}
