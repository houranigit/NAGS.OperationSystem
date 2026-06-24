namespace Identity.Domain.Users;

/// <summary>Lifecycle status of a user account (separate from transient lockout).</summary>
public enum UserStatus
{
    /// <summary>Invited but has not yet set a password / activated.</summary>
    Invited = 1,

    /// <summary>Activated and able to sign in (subject to lockout).</summary>
    Active = 2,

    /// <summary>Deactivated; cannot sign in and is excluded from active operations. Terminal.</summary>
    Deactivated = 3,

    /// <summary>
    /// Temporarily blocked from signing in while the User link is preserved. Reversible: restoring
    /// access returns an activated account to <see cref="Active"/> or requeues an unfinished invitation.
    /// </summary>
    Suspended = 4
}

/// <summary>Result of restoring access to a suspended account.</summary>
public enum AccessRestoreOutcome
{
    /// <summary>An activated account was returned to <see cref="UserStatus.Active"/>.</summary>
    Reactivated = 0,

    /// <summary>An unactivated account returned to <see cref="UserStatus.Invited"/>; requeue its invitation.</summary>
    InvitationRequeued = 1
}
