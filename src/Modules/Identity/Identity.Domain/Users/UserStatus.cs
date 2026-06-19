namespace Identity.Domain.Users;

/// <summary>Lifecycle status of a user account (separate from transient lockout).</summary>
public enum UserStatus
{
    /// <summary>Invited but has not yet set a password / activated.</summary>
    Invited = 1,

    /// <summary>Activated and able to sign in (subject to lockout).</summary>
    Active = 2,

    /// <summary>Deactivated; cannot sign in and is excluded from active operations.</summary>
    Deactivated = 3
}
