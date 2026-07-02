using Identity.Application;
using Identity.Domain.Users;
using Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Identity.IntegrationTests;

public sealed class TokenServiceTests
{
    [Fact]
    public void Mfa_challenge_token_is_bound_to_user_security_stamp()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.CreateActive(
            Email.Create("admin@nags.sa").Value,
            "System Administrator",
            Guid.NewGuid(),
            "hashed-password",
            now).Value;
        var originalStamp = user.SecurityStamp;
        var service = new TokenService(new FixedTimeProvider(now), Options.Create(ValidOptions()));

        var token = service.CreateMfaChallengeToken(user);
        var challenge = service.ValidateMfaChallengeToken(token);
        user.RotateSecurityStamp(now.AddMinutes(1));

        challenge.ShouldNotBeNull();
        challenge!.UserId.ShouldBe(user.Id);
        challenge.SecurityStamp.ShouldBe(originalStamp);
        challenge.SecurityStamp.ShouldNotBe(user.SecurityStamp);
    }

    private static IdentityModuleOptions ValidOptions() =>
        new()
        {
            Jwt = new JwtOptions
            {
                Issuer = "operations-system",
                Audience = "operations-system",
                SigningKey = "integration-tests-signing-key-must-be-long-enough-1234567890"
            }
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
