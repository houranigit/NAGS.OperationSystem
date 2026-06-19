using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Users.Events;

namespace Identity.Domain.Users;

/// <summary>
/// A login account. Created via invitation (no password) and activated by the invitee setting
/// a password. Has exactly one role for v1.0.0. Supports lockout and a status lifecycle.
/// </summary>
public sealed class User : AggregateRoot<Guid>
{
    private User() { }

    public Email Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? PasswordHash { get; private set; }
    public UserStatus Status { get; private set; }
    public Guid RoleId { get; private set; }

    /// <summary>Rotated whenever credentials change; lets existing sessions be invalidated.</summary>
    public Guid SecurityStamp { get; private set; }

    public Guid? InvitationToken { get; private set; }
    public DateTimeOffset? InvitationExpiresAtUtc { get; private set; }

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
        Guid invitationToken,
        DateTimeOffset invitationExpiresAtUtc,
        DateTimeOffset now)
    {
        var nameCheck = ValidateDisplayName(displayName);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        if (roleId == Guid.Empty)
            return Error.Validation("A role is required.", "Identity.User.RoleRequired");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = nameCheck.Value,
            PasswordHash = null,
            Status = UserStatus.Invited,
            RoleId = roleId,
            SecurityStamp = Guid.NewGuid(),
            InvitationToken = invitationToken,
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
            SecurityStamp = Guid.NewGuid(),
            CreatedAtUtc = now
        };

        user.RaiseDomainEvent(new UserInvitedEvent(user.Id, user.Email.Value, user.RoleId));
        user.RaiseDomainEvent(new UserActivatedEvent(user.Id));
        return user;
    }

    public Result Activate(Guid invitationToken, string passwordHash, DateTimeOffset now)
    {
        if (Status != UserStatus.Invited)
            return Error.Conflict("Only an invited account can be activated.", "Identity.User.NotInvited");

        if (InvitationToken is null || InvitationToken != invitationToken)
            return Error.Validation("Invitation token is invalid.", "Identity.User.InvalidInvitation");

        if (InvitationExpiresAtUtc is { } expiry && expiry <= now)
            return Error.Conflict("Invitation has expired.", "Identity.User.InvitationExpired");

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

    public Result ResendInvitation(Guid invitationToken, DateTimeOffset invitationExpiresAtUtc, DateTimeOffset now)
    {
        if (Status != UserStatus.Invited)
            return Error.Conflict("Only an invited account can have its invitation resent.", "Identity.User.NotInvited");

        InvitationToken = invitationToken;
        InvitationExpiresAtUtc = invitationExpiresAtUtc;
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
        UpdatedAtUtc = now;
        RaiseDomainEvent(new UserRoleAssignedEvent(Id, roleId));
        return Result.Success();
    }

    /// <summary>Locks the account indefinitely (manual administrative lock).</summary>
    public Result Lock(DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("Cannot lock a deactivated account.", "Identity.User.Deactivated");

        LockoutEndUtc = DateTimeOffset.MaxValue;
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

    /// <summary>Records a failed sign-in, auto-locking once <paramref name="maxFailedAttempts"/> is reached.</summary>
    public void RecordFailedSignIn(int maxFailedAttempts, TimeSpan lockoutDuration, DateTimeOffset now)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxFailedAttempts)
        {
            LockoutEndUtc = now.Add(lockoutDuration);
            AccessFailedCount = 0;
            RaiseDomainEvent(new UserLockedEvent(Id, LockoutEndUtc));
        }
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
