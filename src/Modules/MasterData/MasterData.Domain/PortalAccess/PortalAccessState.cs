namespace MasterData.Domain.PortalAccess;

/// <summary>
/// The portal-access lifecycle of a MasterData person record (staff member / customer contact) as
/// reflected from the Identity provisioning workflow.
/// </summary>
public enum PortalAccessState
{
    /// <summary>No portal account requested.</summary>
    None = 0,

    /// <summary>A provisioning request has been sent to Identity and is awaiting a reply.</summary>
    Provisioning = 1,

    /// <summary>Identity provisioned an invited account; the person has not activated yet.</summary>
    Invited = 2,

    /// <summary>The linked account is active.</summary>
    Active = 3,

    /// <summary>Access is suspended (e.g. the record or its parent was deactivated).</summary>
    Suspended = 4,

    /// <summary>Provisioning failed; the failure is visible and the grant can be retried.</summary>
    Failed = 5
}
