using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Enumerations;
using Identity.Domain.Events;
using Identity.Domain.Policies;
using Identity.Domain.ValueObjects;

namespace Identity.Domain.Aggregates.User;

public sealed class User : AggregateRoot<UserId>
{
    public Username Username { get; private set; } = null!;
    public Email Email { get; private set; } = null!;
    public PasswordHash? PasswordHash { get; private set; }
    public UserType UserType { get; private set; } = null!;
    public UserStatus Status { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastPasswordChangedAt { get; private set; }
    public DateTime? PasswordExpiresAt { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public string? InvitationToken { get; private set; }
    public DateTime? InvitationTokenExpiresAt { get; private set; }
    public Guid? ExternalReferenceId { get; private set; }

    // Keep IsActive as a computed property for backward compatibility
    public bool IsActive => Status == UserStatus.Active;

    private readonly List<UserRole> _roles = [];
    public IReadOnlyList<UserRole> Roles => _roles.AsReadOnly();

    private User() { }

    /// <summary>Creates a fully active user with a password (e.g. system/admin-created).</summary>
    public static Result<User> Create(
        Username username,
        Email email,
        PasswordHash passwordHash,
        UserType? userType = null,
        PasswordPolicy? passwordPolicy = null)
    {
        var policy = passwordPolicy ?? PasswordPolicy.Default;
        var type = userType ?? UserType.Employee;
        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = UserId.New(),
            Username = username,
            Email = email,
            PasswordHash = passwordHash,
            UserType = type,
            Status = UserStatus.Active,
            CreatedAt = now,
            LastPasswordChangedAt = now,
            PasswordExpiresAt = policy.ExpiryDays.HasValue
                ? now.AddDays(policy.ExpiryDays.Value)
                : null
        };

        user.RaiseDomainEvent(new UserCreatedEvent(user.Id));
        return user;
    }

    /// <summary>Creates an invited user without a password; status PendingActivation.</summary>
    public static Result<User> Invite(
        Username username,
        Email email,
        UserType userType,
        string invitationToken,
        DateTime tokenExpiry,
        Guid? externalReferenceId = null)
    {
        if (string.IsNullOrWhiteSpace(invitationToken))
            return Error.Validation("Invitation token is required.");

        if (tokenExpiry <= DateTime.UtcNow)
            return Error.Validation("Invitation token expiry must be in the future.");

        var user = new User
        {
            Id = UserId.New(),
            Username = username,
            Email = email,
            PasswordHash = null,
            UserType = userType,
            Status = UserStatus.PendingActivation,
            CreatedAt = DateTime.UtcNow,
            InvitationToken = invitationToken,
            InvitationTokenExpiresAt = tokenExpiry,
            ExternalReferenceId = externalReferenceId
        };

        user.RaiseDomainEvent(new UserInvitedEvent(user.Id, email.Value, invitationToken));
        return user;
    }

    /// <summary>
    /// Creates the initial system admin with a fixed token "ADMIN001" and no expiry.
    /// Intended for first-launch seeding only — never call at runtime.
    /// </summary>
    public static Result<User> CreateSeedAdmin(Username username, Email email)
    {
        var user = new User
        {
            Id = UserId.New(),
            Username = username,
            Email = email,
            PasswordHash = null,
            UserType = UserType.SystemAdmin,
            Status = UserStatus.PendingActivation,
            CreatedAt = DateTime.UtcNow,
            InvitationToken = "ADMIN001",
            InvitationTokenExpiresAt = null
        };

        user.RaiseDomainEvent(new UserInvitedEvent(user.Id, email.Value, "ADMIN001"));
        return user;
    }

    /// <summary>
    /// Re-issues the invitation token (and resets expiry) for a user still in <see cref="UserStatus.PendingActivation"/>.
    /// Used by <c>ResendInvitationCommand</c> when the original token expired or the email didn't reach the user.
    /// </summary>
    public Result RegenerateInvitationToken(string newToken, DateTime newExpiry)
    {
        if (Status != UserStatus.PendingActivation)
            return Error.Conflict("User is not in PendingActivation status — cannot regenerate invitation token.");

        if (string.IsNullOrWhiteSpace(newToken))
            return Error.Validation("Invitation token is required.");

        if (newExpiry <= DateTime.UtcNow)
            return Error.Validation("Invitation token expiry must be in the future.");

        InvitationToken = newToken;
        InvitationTokenExpiresAt = newExpiry;
        RaiseDomainEvent(new UserInvitedEvent(Id, Email.Value, newToken));
        return Result.Success();
    }

    /// <summary>Activates an invited user by providing their first password.</summary>
    public Result ActivateFromInvitation(string token, PasswordHash newPasswordHash, PasswordPolicy? policy = null)
    {
        if (Status != UserStatus.PendingActivation)
            return Error.Conflict("User is not in PendingActivation status.");

        if (InvitationToken != token)
            return Error.Validation("Invalid invitation token.");

        if (InvitationTokenExpiresAt.HasValue && DateTime.UtcNow > InvitationTokenExpiresAt.Value)
            return Error.Validation("Invitation token has expired.");

        var pwdPolicy = policy ?? PasswordPolicy.Default;
        var now = DateTime.UtcNow;

        PasswordHash = newPasswordHash;
        Status = UserStatus.Active;
        LastPasswordChangedAt = now;
        PasswordExpiresAt = pwdPolicy.ExpiryDays.HasValue ? now.AddDays(pwdPolicy.ExpiryDays.Value) : null;
        InvitationToken = null;
        InvitationTokenExpiresAt = null;

        RaiseDomainEvent(new UserActivatedEvent(Id));
        return Result.Success();
    }

    /// <summary>Changes the user's password. Requires new hash already validated against history.</summary>
    public Result ChangePassword(PasswordHash newPasswordHash, PasswordPolicy? policy = null)
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("Cannot change password of a deactivated user.");

        var pwdPolicy = policy ?? PasswordPolicy.Default;
        var now = DateTime.UtcNow;

        PasswordHash = newPasswordHash;
        LastPasswordChangedAt = now;
        PasswordExpiresAt = pwdPolicy.ExpiryDays.HasValue ? now.AddDays(pwdPolicy.ExpiryDays.Value) : null;

        // If status was PasswordExpired, restore to Active
        if (Status == UserStatus.PasswordExpired)
            Status = UserStatus.Active;

        RaiseDomainEvent(new UserPasswordChangedEvent(Id));
        return Result.Success();
    }

    /// <summary>Records a failed login attempt; locks user if max attempts exceeded.</summary>
    public Result RecordFailedLogin(LockoutPolicy? lockoutPolicy = null)
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("User is deactivated.");

        var policy = lockoutPolicy ?? LockoutPolicy.Default;
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= policy.MaxFailedAttempts)
        {
            var lockedUntil = DateTime.UtcNow.Add(policy.LockoutDuration);
            Status = UserStatus.Locked;
            LockedUntil = lockedUntil;
            RaiseDomainEvent(new UserLockedEvent(Id, lockedUntil));
        }

        return Result.Success();
    }

    /// <summary>Records a successful login — resets failed attempt counter.</summary>
    public Result RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
        return Result.Success();
    }

    public Result Unlock()
    {
        if (Status != UserStatus.Locked)
            return Error.Conflict("User is not locked.");

        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockedUntil = null;
        RaiseDomainEvent(new UserUnlockedEvent(Id));
        return Result.Success();
    }

    public Result ExpirePassword()
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("User is deactivated.");

        Status = UserStatus.PasswordExpired;
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (Status == UserStatus.Deactivated)
            return Error.Conflict("User is already deactivated.");

        Status = UserStatus.Deactivated;
        RaiseDomainEvent(new UserDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (Status == UserStatus.Active)
            return Error.Conflict("User is already active.");

        Status = UserStatus.Active;
        RaiseDomainEvent(new UserActivatedEvent(Id));
        return Result.Success();
    }

    public Result AssignRole(RoleId roleId)
    {
        if (_roles.Any(r => r.RoleId == roleId))
            return Error.Conflict("Role is already assigned to this user.");

        _roles.Add(UserRole.Create(Id, roleId));
        RaiseDomainEvent(new UserRoleAssignedEvent(Id, roleId));
        return Result.Success();
    }

    public Result RemoveRole(RoleId roleId)
    {
        var existing = _roles.FirstOrDefault(r => r.RoleId == roleId);
        if (existing is null)
            return Error.NotFound("Role is not assigned to this user.");

        _roles.Remove(existing);
        RaiseDomainEvent(new UserRoleRemovedEvent(Id, roleId));
        return Result.Success();
    }

    public Result ChangeEmail(Email newEmail)
    {
        if (Email == newEmail)
            return Error.Conflict("New email is the same as the current email.");

        var oldEmail = Email;
        Email = newEmail;
        RaiseDomainEvent(new UserEmailChangedEvent(Id, oldEmail, newEmail));
        return Result.Success();
    }

    public bool IsLockedOut()
    {
        if (Status == UserStatus.Locked)
        {
            if (LockedUntil.HasValue && DateTime.UtcNow > LockedUntil.Value)
                return false; // auto-unlock eligible
            return true;
        }
        return false;
    }
}
