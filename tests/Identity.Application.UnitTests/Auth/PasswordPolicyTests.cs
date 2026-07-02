using Identity.Application.Features.Auth;
using Identity.Application.Security;
using Shouldly;

namespace Identity.Application.UnitTests.Auth;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Admin#12345")]
    [InlineData("Replacement#12345")]
    public void Strong_passwords_satisfy_the_identity_policy(string password)
    {
        PasswordPolicy.Validate(password, "Password").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("password123!")]
    [InlineData("PASSWORD123!")]
    [InlineData("Password!")]
    [InlineData("Password123")]
    [InlineData("Short#1")]
    public void Weak_passwords_fail_the_identity_policy(string password)
    {
        PasswordPolicy.Validate(password, "Password").ShouldNotBeEmpty();
    }

    [Fact]
    public void Activation_reset_and_change_password_validators_share_the_policy()
    {
        new ActivateAccountCommandValidator()
            .Validate(new ActivateAccountCommand("user@nags.sa", "token", "password123!"))
            .IsValid.ShouldBeFalse();

        new ResetPasswordCommandValidator()
            .Validate(new ResetPasswordCommand("token", "Password123"))
            .IsValid.ShouldBeFalse();

        new ChangePasswordCommandValidator()
            .Validate(new ChangePasswordCommand("Current#12345", "Password!"))
            .IsValid.ShouldBeFalse();
    }
}
