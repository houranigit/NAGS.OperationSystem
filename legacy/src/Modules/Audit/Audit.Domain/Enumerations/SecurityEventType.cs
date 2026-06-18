namespace Audit.Domain.Enumerations;

public enum SecurityEventType
{
    UserCreated,
    UserLoggedIn,
    UserLoginFailed,
    UserLocked,
    UserUnlocked,
    UserActivated,
    UserDeactivated,
    PasswordChanged,
    RoleAssigned,
    RoleRemoved,
    SessionRevoked
}
