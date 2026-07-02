using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Users.Events;

namespace Identity.Domain.Users;

/// <summary>
/// A login account. Created via invitation (no password) and activated by the invitee setting
/// a password. Has exactly one role for v1.0.0. Supports lockout and a status lifecycle.
/// Carries its fixed <see cref="UserType"/> (business identity/data scope) and, for non-admin
/// accounts, the <see cref="ExternalReferenceId"/> of the originating MasterData record.
/// </summary>
public sealed class User : AggregateRoot<Guid>, IAuditable
{
    private readonly List<string> _recoveryCodeHashes = [];

    private User() { }

    string IAuditable.AuditEntityType => "User";
    Guid IAuditable.AuditEntityId => Id;

    public Email Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? PasswordHash { get; private set; }
    public UserStatus Status { get; private set; }
    public Guid RoleId { get; private set; }

    /// <summary>The account's fixed business identity and data scope.</summary>
    public UserType UserType { get; private set; }

    /// <summary>For StationStaff/CustomerContact, the id of the linked MasterData record.</summary>
    public Guid? ExternalReferenceId { get; private set; }

    /// <summary>
    /// True once a permanently-removed account's login email has been released for reuse. The row is
    /// retained for audit, but it no longer participates in login-email uniqueness.
    /// </summary>
    public bool LoginEmailReleased { get; private set; }

    /// <summary>A pending, not-yet-verified new login email (linked email-change workflow).</summary>
    public string? PendingEmail { get; private set; }

    /// <summary>SHA-256 hash of the email-change verification token; the raw token is never stored.</summary>
    public string? EmailChangeToken { get; private set; }
    public DateTimeOffset? EmailChangeExpiresAtUtc { get; private set; }

    /// <summary>Rotated whenever credentials change; lets existing sessions be invalidated.</summary>
    public Guid SecurityStamp { get; private set; }

    /// <summary>True once a TOTP authenticator has been enrolled and confirmed.</summary>
    public bool MfaEnabled { get; private set; }

    /// <summary>Data-Protection-encrypted TOTP secret. Set during enrollment (pending until confirmed).</summary>
    public string? MfaSecret { get; private set; }

    /// <summary>Hashes of the unused one-time recovery codes; a redeemed code is removed.</summary>
    public IReadOnlyList<string> RecoveryCodeHashes => _recoveryCodeHashes;

    /// <summary>System Administrators must use MFA; the requirement is enforced once enrolled at activation.</summary>
    public bool MfaRequired => UserType == UserType.SystemAdministrator;

    /// <summary>SHA-256 hash of the invitation token; the raw token is never stored or returned.</summary>
    public string? InvitationToken { get; private set; }
    public DateTimeOffset? InvitationExpiresAtUtc { get; private set; }

    /// <summary>SHA-256 hash of the active password-reset token; the raw token is never stored.</summary>
    public string? PasswordResetToken { get; private set; }
    public DateTimeOffset? PasswordResetExpiresAtUtc { get; private set; }

    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEndUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndUtc is { } end && end > now;

    public static Result<User> Invite(
        Email email,
        string? displayName,
        Guid roleId,
        string invitationTokenHash,
        DateTimeOffset invitationExpiresAtUtc,
        DateTimeOffset now,
        UserType userType = UserType.SystemAdministrator,
        Guid? externalReferenceId = null)
    {
        var nameCheck = ValidateDisplayName(displayName);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        if (roleId == Guid.Empty)
            return Error.Validation("A role is required.", "Identity.User.RoleRequired");

        if (userType != UserType.SystemAdministrator && (externalReferenceId is null || externalReferenceId == Guid.Empty))
            return Error.Validation("A non-administrator account requires an external reference.", "Identity.User.ExternalReferenceRequired");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = nameCheck.Value,
            PasswordHash = null,
            Status = UserStatus.Invited,
            RoleId = roleId,
            UserType = userType,
            ExternalReferenceId = externalReferenceId,
            SecurityStamp = Guid.NewGuid(),
            InvitationToken = invitationTokenHash,
            InvitationExpiresAtUtc = invitationExpiresAtUtc,
            CreatedAtUtc = now
        };

        user.RaiseDomainEvent(new UserInvitedEvent(user.Id, user.Email.Value, user.RoleId));
        return user;
    }

    /// <summary>
    /// Creates an already-active user with a password set. Used to bootstrap the seeded
    /// System Admin account where there is no invitation flow.
    /// </summary>
    public static Result<User> CreateActive(
        Email email,
        string? displayName,
        Guid roleId,
        string passwordHash,
        DateTimeOffset now)
    {
        var nameCheck = ValidateDisplayName(displayName);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        if (roleId == Guid.Empty)
            return Error.Validation("A role is required.", "Identity.User.RoleRequired");

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Error.Validation("Password hash is required.", "Identity.User.PasswordRequired");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = nameCheck.Value,
            PasswordHash = passwordHash,
            Status = UserStatus.Active,
            RoleId = roleId,
            UserType = UserType.SystemAdministrator,
            SecurityStamp = Guid.NewGuid(),
            CreatedAtUtc = now
        };

        user.RaiseDomainEvent(new UserInvitedEvent(user.Id, user.Email.Value, user.RoleId));
        user.RaiseDomainEvent(new UserActivatedEvent(user.Id));
        return user;
    }

    /// <summary>
    /// Starts a linked email-change. The login email does not change until the new address is
    /// verified, so a typo or undeliverable address cannot lock the account out.
    /// </summary>
    public Result RequestEmailChange(Email newEmail, string tokenHash, DateTimeOffset expiresAtUtc, DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("Cannot change the email of a deactivated account.", "Identity.User.Deactivated");

        if (string.Equals(newEmail.Value, Email.Value, StringComparison.Ordinal))
            return Error.Validation("The new email matches the current email.", "Identity.User.EmailUnchanged");

        PendingEmail = newEmail.Value;
        EmailChangeToken = tokenHash;
        EmailChangeExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Completes a linked email-change after the new address is verified.</summary>
    public Result ConfirmEmailChange(Email verifiedEmail, DateTimeOffset now)
    {
        if (PendingEmail is null || !string.Equals(PendingEmail, verifiedEmail.Value, StringComparison.Ordinal))
            return Error.Conflict("There is no pending email change for this address.", "Identity.User.NoPendingEmailChange");

        Email = verifiedEmail;
        PendingEmail = null;
        EmailChangeToken = null;
        EmailChangeExpiresAtUtc = null;
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserProfileUpdatedEvent(Id));
        return Result.Success();
    }

    /// <summary>
    /// Permanently detaches the account from its MasterData identity and releases its login email
    /// for reuse by a different identity. The row and its historical email are retained for audit.
    /// </summary>
    public Result ReleaseLoginEmail(DateTimeOffset now)
    {
        Status = UserStatus.Deactivated;
        LoginEmailReleased = true;
        ExternalReferenceId = null;
        PendingEmail = null;
        EmailChangeToken = null;
        EmailChangeExpiresAtUtc = null;
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result Activate(string invitationTokenHash, string passwordHash, DateTimeOffset now)
    {
        var invitationCheck = ValidateInvitation(invitationTokenHash, now);
        if (invitationCheck.IsFailure)
            return invitationCheck.Error;

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Error.Validation("Password hash is required.", "Identity.User.PasswordRequired");

        PasswordHash = passwordHash;
        Status = UserStatus.Active;
        InvitationToken = null;
        InvitationExpiresAtUtc = null;
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserActivatedEvent(Id));
        return Result.Success();
    }

    /// <summary>Checks whether an invitation token can currently activate this account.</summary>
    public Result ValidateInvitation(string invitationTokenHash, DateTimeOffset now)
    {
        if (Status != UserStatus.Invited)
            return Error.Conflict("Only an invited account can be activated.", "Identity.User.NotInvited");

        if (InvitationToken is null || !string.Equals(InvitationToken, invitationTokenHash, StringComparison.Ordinal))
            return Error.Validation("Invitation token is invalid.", "Identity.User.InvalidInvitation");

        if (InvitationExpiresAtUtc is { } expiry && expiry <= now)
            return Error.Conflict("Invitation has expired.", "Identity.User.InvitationExpired");

        return Result.Success();
    }

    public Result ResendInvitation(string invitationTokenHash, DateTimeOffset invitationExpiresAtUtc, DateTimeOffset now)
    {
        if (Status != UserStatus.Invited)
            return Error.Conflict("Only an invited account can have its invitation resent.", "Identity.User.NotInvited");

        InvitationToken = invitationTokenHash;
        InvitationExpiresAtUtc = invitationExpiresAtUtc;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>
    /// Begins a password reset by storing the hash of a freshly issued token. Only an active account
    /// can reset its password; invited accounts activate instead, and deactivated accounts cannot.
    /// </summary>
    public Result RequestPasswordReset(string tokenHash, DateTimeOffset expiresAtUtc, DateTimeOffset now)
    {
        if (Status != UserStatus.Active)
            return Error.Conflict("Only an active account can reset its password.", "Identity.User.NotActive");

        PasswordResetToken = tokenHash;
        PasswordResetExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Completes a password reset, rotating the security stamp so existing sessions are invalidated.</summary>
    public Result ResetPassword(string tokenHash, string passwordHash, DateTimeOffset now)
    {
        var resetCheck = ValidatePasswordReset(tokenHash, now);
        if (resetCheck.IsFailure)
            return resetCheck.Error;

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Error.Validation("Password hash is required.", "Identity.User.PasswordRequired");

        PasswordHash = passwordHash;
        PasswordResetToken = null;
        PasswordResetExpiresAtUtc = null;
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserPasswordChangedEvent(Id));
        return Result.Success();
    }

    /// <summary>Checks whether a password-reset token can currently set a new password.</summary>
    public Result ValidatePasswordReset(string tokenHash, DateTimeOffset now)
    {
        if (Status != UserStatus.Active || PasswordResetToken is null)
            return Error.Validation("The reset link is invalid or has expired.", "Identity.User.InvalidReset");

        if (!string.Equals(PasswordResetToken, tokenHash, StringComparison.Ordinal))
            return Error.Validation("The reset link is invalid or has expired.", "Identity.User.InvalidReset");

        if (PasswordResetExpiresAtUtc is { } expiry && expiry <= now)
            return Error.Validation("The reset link is invalid or has expired.", "Identity.User.InvalidReset");

        return Result.Success();
    }

    /// <summary>
    /// Stores a freshly generated (encrypted) TOTP secret as a pending enrollment. MFA is not yet in
    /// effect; the user must confirm with a valid code first.
    /// </summary>
    public Result BeginMfaEnrollment(string encryptedSecret, DateTimeOffset now)
    {
        if (Status != UserStatus.Active)
            return Error.Conflict("Only an active account can enroll MFA.", "Identity.User.NotActive");

        if (MfaEnabled)
            return Error.Conflict("MFA is already enabled. Reset it before starting a new enrollment.", "Identity.User.MfaAlreadyEnabled");

        if (string.IsNullOrWhiteSpace(encryptedSecret))
            return Error.Validation("An MFA secret is required.", "Identity.User.MfaSecretRequired");

        MfaSecret = encryptedSecret;
        MfaEnabled = false;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>
    /// Confirms a pending enrollment after a valid code was verified, enabling MFA and replacing the
    /// recovery codes. Rotates the security stamp.
    /// </summary>
    public Result ConfirmMfaEnrollment(IEnumerable<string> recoveryCodeHashes, DateTimeOffset now)
    {
        if (MfaSecret is null)
            return Error.Conflict("There is no pending MFA enrollment to confirm.", "Identity.User.NoPendingMfa");

        MfaEnabled = true;
        _recoveryCodeHashes.Clear();
        _recoveryCodeHashes.AddRange(recoveryCodeHashes);

        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Redeems an unused recovery code (by hash). Returns failure if no matching code exists.</summary>
    public Result ConsumeRecoveryCode(string codeHash, DateTimeOffset now)
    {
        if (!_recoveryCodeHashes.Remove(codeHash))
            return Error.Validation("Invalid recovery code.", "Identity.User.InvalidRecoveryCode");

        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Administrative MFA reset: clears the authenticator and recovery codes; the user must re-enroll.</summary>
    public Result ResetMfa(DateTimeOffset now)
    {
        MfaEnabled = false;
        MfaSecret = null;
        _recoveryCodeHashes.Clear();
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result ChangePassword(string passwordHash, DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("Cannot change the password of a deactivated account.", "Identity.User.Deactivated");

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Error.Validation("Password hash is required.", "Identity.User.PasswordRequired");

        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserPasswordChangedEvent(Id));
        return Result.Success();
    }

    public Result UpdateProfile(string? displayName, DateTimeOffset now)
    {
        var nameCheck = ValidateDisplayName(displayName);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        DisplayName = nameCheck.Value;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserProfileUpdatedEvent(Id));
        return Result.Success();
    }

    public Result AssignRole(Guid roleId, DateTimeOffset now)
    {
        if (roleId == Guid.Empty)
            return Error.Validation("A role is required.", "Identity.User.RoleRequired");

        RoleId = roleId;
        // A role change alters the permission set; rotate the stamp so outstanding access tokens
        // (which carry the old permissions) are rejected on their next request.
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserRoleAssignedEvent(Id, roleId));
        return Result.Success();
    }

    /// <summary>Rotates the security stamp without other state changes (e.g. when the role's permissions change).</summary>
    public void RotateSecurityStamp(DateTimeOffset now)
    {
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
    }

    /// <summary>Locks the account indefinitely (manual administrative lock).</summary>
    public Result Lock(DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("Cannot lock a deactivated account.", "Identity.User.Deactivated");

        LockoutEndUtc = DateTimeOffset.MaxValue;
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserLockedEvent(Id, LockoutEndUtc));
        return Result.Success();
    }

    public Result Unlock(DateTimeOffset now)
    {
        LockoutEndUtc = null;
        AccessFailedCount = 0;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserUnlockedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
            return Result.Success();

        Status = UserStatus.Deactivated;
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserDeactivatedEvent(Id));
        return Result.Success();
    }

    /// <summary>
    /// Reversibly blocks sign-in while preserving the User link. The security stamp is rotated so
    /// outstanding access tokens are rejected; the caller revokes sessions.
    /// </summary>
    public Result Suspend(DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("Cannot suspend a deactivated account.", "Identity.User.Deactivated");

        if (Status == UserStatus.Suspended)
            return Result.Success();

        Status = UserStatus.Suspended;
        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>
    /// Restores access to a suspended account. An activated account (one that has set a password)
    /// returns to <see cref="UserStatus.Active"/>; an account suspended before it was ever activated
    /// returns to <see cref="UserStatus.Invited"/> and reports that its invitation must be requeued.
    /// </summary>
    public Result<AccessRestoreOutcome> RestoreAccess(DateTimeOffset now)
    {
        if (Status != UserStatus.Suspended)
            return Error.Conflict("Only a suspended account can have its access restored.", "Identity.User.NotSuspended");

        SecurityStamp = Guid.NewGuid();
        UpdatedAtUtc = now;

        if (PasswordHash is not null)
        {
            Status = UserStatus.Active;
            return AccessRestoreOutcome.Reactivated;
        }

        Status = UserStatus.Invited;
        return AccessRestoreOutcome.InvitationRequeued;
    }

    /// <summary>
    /// Records a failed sign-in, auto-locking once <paramref name="maxFailedAttempts"/> is reached.
    /// Returns true when this attempt locked the account so callers can revoke active sessions.
    /// </summary>
    public bool RecordFailedSignIn(int maxFailedAttempts, TimeSpan lockoutDuration, DateTimeOffset now)
    {
        AccessFailedCount++;
        UpdatedAtUtc = now;
        if (AccessFailedCount >= maxFailedAttempts)
        {
            LockoutEndUtc = now.Add(lockoutDuration);
            AccessFailedCount = 0;
            SecurityStamp = Guid.NewGuid();
            RaiseDomainEvent(new UserLockedEvent(Id, LockoutEndUtc));
            return true;
        }

        return false;
    }

    public void RecordSuccessfulSignIn(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LastLoginAtUtc = now;
    }

    private static Result<string> ValidateDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Error.Validation("Display name is required.", "Identity.User.DisplayNameRequired");

        var trimmed = displayName.Trim();
        if (trimmed.Length > 150)
            return Error.Validation("Display name must not exceed 150 characters.", "Identity.User.DisplayNameTooLong");

        return trimmed;
    }
}
