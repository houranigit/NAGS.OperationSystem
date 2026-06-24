using Identity.Domain.Users;
using Identity.Domain.Users.Events;
using Shouldly;

namespace Identity.Domain.UnitTests.Users;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid RoleId = Guid.NewGuid();

    private static Email AnEmail(string value = "user@nags.sa") => Email.Create(value).Value;

    private const string TokenHash = "TOKEN-HASH-AAAA";

    [Fact]
    public void Invite_creates_invited_user_without_password_and_raises_event()
    {
        var result = User.Invite(AnEmail(), "Test User", RoleId, TokenHash, Now.AddHours(72), Now);

        result.IsSuccess.ShouldBeTrue();
        var user = result.Value;
        user.Status.ShouldBe(UserStatus.Invited);
        user.PasswordHash.ShouldBeNull();
        user.InvitationToken.ShouldBe(TokenHash);
        user.DomainEvents.OfType<UserInvitedEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Activate_with_correct_token_activates_and_clears_invitation()
    {
        var user = User.Invite(AnEmail(), "Test User", RoleId, TokenHash, Now.AddHours(72), Now).Value;

        var result = user.Activate(TokenHash, "hashed-password", Now.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Active);
        user.PasswordHash.ShouldBe("hashed-password");
        user.InvitationToken.ShouldBeNull();
        user.DomainEvents.OfType<UserActivatedEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Activate_with_wrong_token_fails()
    {
        var user = User.Invite(AnEmail(), "Test User", RoleId, TokenHash, Now.AddHours(72), Now).Value;

        var result = user.Activate("DIFFERENT-HASH", "hashed", Now.AddHours(1));

        result.IsFailure.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Invited);
    }

    [Fact]
    public void Activate_after_expiry_fails()
    {
        var user = User.Invite(AnEmail(), "Test User", RoleId, TokenHash, Now.AddHours(1), Now).Value;

        var result = user.Activate(TokenHash, "hashed", Now.AddHours(2));

        result.IsFailure.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Invited);
    }

    [Fact]
    public void RecordFailedSignIn_locks_account_after_reaching_max_attempts()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;

        for (var i = 0; i < 5; i++)
            user.RecordFailedSignIn(maxFailedAttempts: 5, TimeSpan.FromMinutes(15), Now);

        user.IsLockedOut(Now.AddMinutes(1)).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(20)).ShouldBeFalse();
    }

    [Fact]
    public void Lock_then_Unlock_restores_access()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;

        user.Lock(Now);
        user.IsLockedOut(Now.AddYears(1)).ShouldBeTrue();

        user.Unlock(Now);
        user.IsLockedOut(Now.AddYears(1)).ShouldBeFalse();
        user.AccessFailedCount.ShouldBe(0);
    }

    [Fact]
    public void Deactivate_blocks_password_change()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;

        user.Deactivate(Now);
        user.Status.ShouldBe(UserStatus.Deactivated);

        var result = user.ChangePassword("new-hash", Now);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Suspend_blocks_sign_in_and_rotates_security_stamp()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        var originalStamp = user.SecurityStamp;

        var result = user.Suspend(Now);

        result.IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Suspended);
        user.SecurityStamp.ShouldNotBe(originalStamp);
    }

    [Fact]
    public void Suspend_then_RestoreAccess_returns_activated_user_to_active()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        user.Suspend(Now);

        var result = user.RestoreAccess(Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(AccessRestoreOutcome.Reactivated);
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void Suspend_before_activation_then_restore_requeues_invitation()
    {
        var user = User.Invite(AnEmail(), "Pending", RoleId, TokenHash, Now.AddHours(72), Now).Value;
        user.Suspend(Now);

        var result = user.RestoreAccess(Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(AccessRestoreOutcome.InvitationRequeued);
        user.Status.ShouldBe(UserStatus.Invited);
    }

    [Fact]
    public void Suspend_fails_for_deactivated_account()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        user.Deactivate(Now);

        user.Suspend(Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RestoreAccess_fails_when_not_suspended()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;

        user.RestoreAccess(Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void AssignRole_rotates_security_stamp()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        var originalStamp = user.SecurityStamp;

        user.AssignRole(Guid.NewGuid(), Now);

        user.SecurityStamp.ShouldNotBe(originalStamp);
    }
}
