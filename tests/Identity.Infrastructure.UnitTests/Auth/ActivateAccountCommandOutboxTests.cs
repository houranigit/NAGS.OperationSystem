using System.Text.Json;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Abstractions;
using Identity.Application.Features.Auth;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Auth;

public sealed class ActivateAccountCommandOutboxTests
{
    private const string RawInvitationToken = "raw-invitation-token";

    [Theory]
    [InlineData(UserType.StationStaff)]
    [InlineData(UserType.CustomerContact)]
    public async Task Activating_linked_user_enqueues_typed_portal_activation(UserType userType)
    {
        await using var db = CreateDb();
        var user = await AddInvitedLinkedUserAsync(db, userType);

        var result = await new ActivateAccountCommandHandler(
                db,
                new TestPasswordHasher(),
                new TestTokenService(),
                TimeProvider.System)
            .Handle(new ActivateAccountCommand(user.Email.Value, RawInvitationToken, "Password#12345"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var activated = await db.Users.SingleAsync(u => u.Id == user.Id);
        activated.Status.ShouldBe(UserStatus.Active);

        var message = await db.OutboxMessages.SingleAsync(m => m.Type.Contains(nameof(PortalUserActivated)));
        var integrationEvent = JsonSerializer.Deserialize<PortalUserActivated>(message.Content);

        integrationEvent.ShouldNotBeNull();
        integrationEvent.ExternalReferenceId.ShouldBe(user.ExternalReferenceId!.Value);
        integrationEvent.UserId.ShouldBe(user.Id);
        integrationEvent.UserType.ShouldBe(userType);
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-activation-{Guid.NewGuid():N}")
            .Options);

    private static async Task<User> AddInvitedLinkedUserAsync(IdentityDbContext db, UserType userType)
    {
        var now = TimeProvider.System.GetUtcNow();
        var role = Role.Create($"{userType} Role", null, [], userType, now).Value;
        var user = User.Invite(
            Email.Create($"{userType.ToString().ToLowerInvariant()}@example.com").Value,
            $"{userType} User",
            role.Id,
            new TestTokenService().HashToken(RawInvitationToken),
            now.AddHours(24),
            now,
            userType,
            Guid.NewGuid()).Value;

        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string providedPassword) =>
            passwordHash == Hash(providedPassword);
    }

    private sealed class TestTokenService : ITokenService
    {
        public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions, Guid sessionId) =>
            throw new NotSupportedException();

        public RefreshToken CreateRefreshToken() =>
            throw new NotSupportedException();

        public string HashRefreshToken(string rawToken) => $"hash-refresh:{rawToken}";

        public SecureToken CreateSecureToken() => new("raw-token", "hash:raw-token");

        public string HashToken(string rawToken) => $"hash:{rawToken}";

        public string CreateMfaChallengeToken(User user) =>
            throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }
}
