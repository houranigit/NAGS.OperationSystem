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
    public void ResendInvitation_for_invited_user_rotates_token_and_expiry()
    {
        var user = User.Invite(AnEmail(), "Test User", RoleId, TokenHash, Now.AddHours(1), Now).Value;

        var result = user.ResendInvitation("TOKEN-HASH-BBBB", Now.AddHours(72), Now.AddHours(2));

        result.IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Invited);
        user.InvitationToken.ShouldBe("TOKEN-HASH-BBBB");
        user.InvitationExpiresAtUtc.ShouldBe(Now.AddHours(72));
        user.UpdatedAtUtc.ShouldBe(Now.AddHours(2));
    }

    [Fact]
    public void ResendInvitation_for_active_user_fails_without_changing_token()
    {
        var user = User.Invite(AnEmail(), "Test User", RoleId, TokenHash, Now.AddHours(72), Now).Value;
        user.Activate(TokenHash, "hashed-password", Now.AddHours(1)).IsSuccess.ShouldBeTrue();

        var result = user.ResendInvitation("TOKEN-HASH-BBBB", Now.AddHours(72), Now.AddHours(2));

        result.IsFailure.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Active);
        user.InvitationToken.ShouldBeNull();
    }

    [Fact]
    public void ValidateInvitation_with_correct_token_succeeds_without_activating()
    {
        var user = User.Invite(AnEmail(), "Test User", RoleId, TokenHash, Now.AddHours(72), Now).Value;

        var result = user.ValidateInvitation(TokenHash, Now.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Invited);
        user.PasswordHash.ShouldBeNull();
        user.InvitationToken.ShouldBe(TokenHash);
    }

    [Fact]
    public void ValidateInvitation_with_wrong_token_fails_without_changing_state()
    {
        var user = User.Invite(AnEmail(), "Test User", RoleId, TokenHash, Now.AddHours(72), Now).Value;

        var result = user.ValidateInvitation("DIFFERENT-HASH", Now.AddHours(1));

        result.IsFailure.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Invited);
        user.InvitationToken.ShouldBe(TokenHash);
    }

    [Fact]
    public void ValidatePasswordReset_with_expired_token_fails_without_clearing_token()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        user.RequestPasswordReset(TokenHash, Now.AddHours(1), Now).IsSuccess.ShouldBeTrue();

        var result = user.ValidatePasswordReset(TokenHash, Now.AddHours(2));

        result.IsFailure.ShouldBeTrue();
        user.PasswordResetToken.ShouldBe(TokenHash);
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void RecordFailedSignIn_locks_account_after_reaching_max_attempts()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        var originalStamp = user.SecurityStamp;

        bool locked = false;
        for (var i = 0; i < 5; i++)
            locked = user.RecordFailedSignIn(maxFailedAttempts: 5, TimeSpan.FromMinutes(15), Now);

        locked.ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(1)).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(20)).ShouldBeFalse();
        user.SecurityStamp.ShouldNotBe(originalStamp);
        user.AccessFailedCount.ShouldBe(0);
        user.UpdatedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Lock_then_Unlock_restores_access()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        var originalStamp = user.SecurityStamp;

        user.Lock(Now);
        user.IsLockedOut(Now.AddYears(1)).ShouldBeTrue();
        user.SecurityStamp.ShouldNotBe(originalStamp);

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
    public void ReleaseLoginEmail_detaches_external_reference_and_allows_email_reuse()
    {
        var externalReferenceId = Guid.NewGuid();
        var user = User.Invite(
            AnEmail(),
            "Station Staff",
            RoleId,
            TokenHash,
            Now.AddHours(72),
            Now,
            BuildingBlocks.Contracts.Authorization.UserType.StationStaff,
            externalReferenceId).Value;

        var result = user.ReleaseLoginEmail(Now.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Deactivated);
        user.LoginEmailReleased.ShouldBeTrue();
        user.ExternalReferenceId.ShouldBeNull();
        user.PendingEmail.ShouldBeNull();
        user.EmailChangeToken.ShouldBeNull();
        user.Email.Value.ShouldBe("user@nags.sa");
    }

    [Fact]
    public void ClearPendingEmailChange_removes_stale_email_change_token_without_changing_login_email()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        user.RequestEmailChange(AnEmail("new@nags.sa"), "EMAIL-HASH", Now.AddHours(2), Now.AddMinutes(1))
            .IsSuccess.ShouldBeTrue();

        user.ClearPendingEmailChange(Now.AddMinutes(2));

        user.Email.Value.ShouldBe("user@nags.sa");
        user.PendingEmail.ShouldBeNull();
        user.EmailChangeToken.ShouldBeNull();
        user.EmailChangeExpiresAtUtc.ShouldBeNull();
        user.UpdatedAtUtc.ShouldBe(Now.AddMinutes(2));
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

    [Fact]
    public void ResetMfa_clears_authenticator_recovery_codes_and_rotates_security_stamp()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        user.BeginMfaEnrollment("encrypted-secret", Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        user.ConfirmMfaEnrollment(["hash-1", "hash-2"], Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        var originalStamp = user.SecurityStamp;

        var result = user.ResetMfa(Now.AddMinutes(3));

        result.IsSuccess.ShouldBeTrue();
        user.MfaEnabled.ShouldBeFalse();
        user.MfaSecret.ShouldBeNull();
        user.RecoveryCodeHashes.ShouldBeEmpty();
        user.SecurityStamp.ShouldNotBe(originalStamp);
    }

    [Fact]
    public void ConsumeRecoveryCode_removes_matching_code_once()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        user.BeginMfaEnrollment("encrypted-secret", Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        user.ConfirmMfaEnrollment(["hash-1", "hash-2"], Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        var firstUse = user.ConsumeRecoveryCode("hash-1", Now.AddMinutes(3));
        var secondUse = user.ConsumeRecoveryCode("hash-1", Now.AddMinutes(4));

        firstUse.IsSuccess.ShouldBeTrue();
        secondUse.IsFailure.ShouldBeTrue();
        user.RecoveryCodeHashes.ShouldBe(["hash-2"]);
    }

    [Fact]
    public void ConsumeRecoveryCode_with_wrong_code_keeps_existing_codes()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        user.BeginMfaEnrollment("encrypted-secret", Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        user.ConfirmMfaEnrollment(["hash-1", "hash-2"], Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        var result = user.ConsumeRecoveryCode("missing-hash", Now.AddMinutes(3));

        result.IsFailure.ShouldBeTrue();
        user.RecoveryCodeHashes.ShouldBe(["hash-1", "hash-2"]);
    }

    [Fact]
    public void BeginMfaEnrollment_fails_when_mfa_is_already_enabled()
    {
        var user = User.CreateActive(AnEmail(), "Admin", RoleId, "hash", Now).Value;
        user.BeginMfaEnrollment("encrypted-secret", Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        user.ConfirmMfaEnrollment(["hash-1"], Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        var result = user.BeginMfaEnrollment("new-encrypted-secret", Now.AddMinutes(3));

        result.IsFailure.ShouldBeTrue();
        user.MfaEnabled.ShouldBeTrue();
        user.MfaSecret.ShouldBe("encrypted-secret");
    }
}
