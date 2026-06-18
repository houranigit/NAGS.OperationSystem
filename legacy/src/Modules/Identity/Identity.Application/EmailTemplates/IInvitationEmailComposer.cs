using BuildingBlocks.Application.Abstractions;

namespace Identity.Application.EmailTemplates;

/// <summary>
/// Builds the transactional emails that Identity sends — kept behind an abstraction so the host
/// supplies the portal base URL / branding, and so Identity tests can stub out template rendering.
/// </summary>
public interface IInvitationEmailComposer
{
    /// <summary>Builds the invite email with a one-click activation link.</summary>
    EmailMessage BuildInvitation(
        string recipientEmail,
        string recipientDisplayName,
        string invitationToken,
        DateTime expiresAtUtc);
}
