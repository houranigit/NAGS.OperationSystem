using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Features.Roles;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Roles;

public sealed class RoleMutationAuthorizationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 18, 0, 0, TimeSpan.Zero);

    private static readonly string[] AllRoleMutationPermissions =
    [
        IdentityPermissions.Roles.Create,
        IdentityPermissions.Roles.Update,
        IdentityPermissions.Roles.Delete,
        IdentityPermissions.Roles.ManagePermissions,
        IdentityPermissions.Roles.View
    ];

    [Theory]
    [InlineData(MutationKind.Create)]
    [InlineData(MutationKind.Update)]
    [InlineData(MutationKind.UpdatePermissions)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    [InlineData(MutationKind.Delete)]
    public async Task Mutation_revalidates_the_actors_live_role_after_taking_the_access_lock(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var seed = await SeedAsync(inner, actorPermissions: []);
        var db = new TrackingIdentityDbContext(inner);

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, AllRoleMutationPermissions),
            seed.TargetRole);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AccessManagementForbidden");
        db.BeginCount.ShouldBe(1);
        db.CommitCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(MutationKind.Create)]
    [InlineData(MutationKind.Update)]
    [InlineData(MutationKind.UpdatePermissions)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    [InlineData(MutationKind.Delete)]
    public async Task Mutation_requires_the_endpoint_permission_in_the_actor_claims(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var seed = await SeedAsync(inner, AllRoleMutationPermissions);
        var db = new TrackingIdentityDbContext(inner);

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, []),
            seed.TargetRole);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AccessManagementForbidden");
        db.BeginCount.ShouldBe(1);
        db.CommitCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(MutationKind.Create)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    public async Task Combined_mutation_revalidates_the_second_permission_against_live_role(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var livePermissions = mutation == MutationKind.Create
            ? new[] { IdentityPermissions.Roles.Create }
            : new[] { IdentityPermissions.Roles.Update };
        var seed = await SeedAsync(inner, livePermissions);
        var db = new TrackingIdentityDbContext(inner);

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, AllRoleMutationPermissions),
            seed.TargetRole);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AccessManagementForbidden");
        db.BeginCount.ShouldBe(1);
        db.CommitCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(MutationKind.Create)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    public async Task Combined_mutation_revalidates_the_second_permission_against_claims(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var claimedPermissions = mutation == MutationKind.Create
            ? new[] { IdentityPermissions.Roles.Create }
            : new[] { IdentityPermissions.Roles.Update };
        var seed = await SeedAsync(inner, AllRoleMutationPermissions);
        var db = new TrackingIdentityDbContext(inner);

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, claimedPermissions),
            seed.TargetRole);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AccessManagementForbidden");
        db.BeginCount.ShouldBe(1);
        db.CommitCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(MutationKind.Create)]
    [InlineData(MutationKind.UpdatePermissions)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    public async Task Permission_grant_cannot_exceed_the_actors_live_role_ceiling(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var seed = await SeedAsync(inner, AllRoleMutationPermissions);
        var db = new TrackingIdentityDbContext(inner);
        var claims = AllRoleMutationPermissions
            .Append(IdentityPermissions.Users.Deactivate)
            .ToArray();

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, claims),
            seed.TargetRole,
            [IdentityPermissions.Users.Deactivate]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.PermissionDelegationForbidden");
        seed.TargetRole.Permissions.ShouldBeEmpty();
        db.CommitCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(MutationKind.Create)]
    [InlineData(MutationKind.UpdatePermissions)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    public async Task Permission_grant_cannot_exceed_the_actors_claim_ceiling(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var livePermissions = AllRoleMutationPermissions
            .Append(IdentityPermissions.Users.Deactivate)
            .ToArray();
        var seed = await SeedAsync(inner, livePermissions);
        var db = new TrackingIdentityDbContext(inner);

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, AllRoleMutationPermissions),
            seed.TargetRole,
            [IdentityPermissions.Users.Deactivate]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.PermissionDelegationForbidden");
        seed.TargetRole.Permissions.ShouldBeEmpty();
        db.CommitCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(MutationKind.UpdatePermissions)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    public async Task Permission_update_rejects_a_current_role_above_the_actors_ceiling(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var seed = await SeedAsync(
            inner,
            AllRoleMutationPermissions,
            targetPermissions: [IdentityPermissions.Users.Deactivate]);
        var db = new TrackingIdentityDbContext(inner);

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, AllRoleMutationPermissions),
            seed.TargetRole,
            [IdentityPermissions.Roles.View]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.ManagedRoleForbidden");
        seed.TargetRole.Permissions.ShouldBe([IdentityPermissions.Users.Deactivate]);
        db.CommitCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(MutationKind.Create)]
    [InlineData(MutationKind.Update)]
    [InlineData(MutationKind.UpdatePermissions)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    [InlineData(MutationKind.Delete)]
    public async Task Successful_mutation_commits_the_serialized_transaction(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var seed = await SeedAsync(inner, AllRoleMutationPermissions);
        var db = new TrackingIdentityDbContext(inner);

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, AllRoleMutationPermissions),
            seed.TargetRole);

        result.IsSuccess.ShouldBeTrue();
        db.BeginCount.ShouldBe(1);
        db.CommitCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(MutationKind.Create)]
    [InlineData(MutationKind.Update)]
    [InlineData(MutationKind.UpdatePermissions)]
    [InlineData(MutationKind.UpdateAndPermissions)]
    [InlineData(MutationKind.Delete)]
    public async Task Mutation_maps_optimistic_concurrency_failures_to_stale(
        MutationKind mutation)
    {
        await using var inner = CreateDb();
        var seed = await SeedAsync(inner, AllRoleMutationPermissions);
        var db = new TrackingIdentityDbContext(
            inner,
            new DbUpdateConcurrencyException("Synthetic stale write."));

        var result = await InvokeAsync(
            mutation,
            db,
            Context(seed.Actor.Id, AllRoleMutationPermissions),
            seed.TargetRole);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("General.ConcurrencyConflict");
        db.BeginCount.ShouldBe(1);
        db.CommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task Delete_maps_a_foreign_key_assignment_race_to_role_in_use()
    {
        await using var inner = CreateDb();
        var seed = await SeedAsync(inner, AllRoleMutationPermissions);
        var db = new TrackingIdentityDbContext(
            inner,
            new DbUpdateException("Synthetic restrictive-FK failure."));

        var result = await new DeleteRoleCommandHandler(
                db,
                Context(seed.Actor.Id, AllRoleMutationPermissions),
                Registry(),
                new FixedTimeProvider(Now))
            .Handle(new DeleteRoleCommand(seed.TargetRole.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Role.InUse");
        db.BeginCount.ShouldBe(1);
        db.CommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task Permission_mutation_preserves_the_own_role_guard()
    {
        await using var inner = CreateDb();
        var seed = await SeedAsync(inner, AllRoleMutationPermissions);
        var db = new TrackingIdentityDbContext(inner);

        var result = await new UpdateRolePermissionsCommandHandler(
                db,
                Context(seed.Actor.Id, AllRoleMutationPermissions),
                Registry(),
                NoopInvitationNotifier.Instance,
                new TestTokenService(),
                new FixedTimeProvider(Now),
                Options.Create(new IdentityModuleOptions()),
                NullLogger<UpdateRolePermissionsCommandHandler>.Instance)
            .Handle(
                new UpdateRolePermissionsCommand(
                    seed.ActorRole.Id,
                    [IdentityPermissions.Roles.View]),
                CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Role.CannotModifyOwnPermissions");
        db.CommitCount.ShouldBe(0);
    }

    private static async Task<Result> InvokeAsync(
        MutationKind mutation,
        IIdentityDbContext db,
        IUserContext context,
        Role targetRole,
        IReadOnlyList<string>? requestedPermissions = null)
    {
        var registry = Registry();
        var clock = new FixedTimeProvider(Now);
        requestedPermissions ??= [IdentityPermissions.Roles.View];

        return mutation switch
        {
            MutationKind.Create => await new CreateRoleCommandHandler(
                    db,
                    context,
                    registry,
                    clock)
                .Handle(
                    new CreateRoleCommand(
                        $"Created-{Guid.NewGuid():N}",
                        null,
                        UserType.SystemAdministrator,
                        requestedPermissions),
                    CancellationToken.None),

            MutationKind.Update => await new UpdateRoleCommandHandler(
                    db,
                    context,
                    registry,
                    clock)
                .Handle(
                    new UpdateRoleCommand(
                        targetRole.Id,
                        $"Updated-{Guid.NewGuid():N}",
                        "Updated"),
                    CancellationToken.None),

            MutationKind.UpdatePermissions => await new UpdateRolePermissionsCommandHandler(
                    db,
                    context,
                    registry,
                    NoopInvitationNotifier.Instance,
                    new TestTokenService(),
                    clock,
                    Options.Create(new IdentityModuleOptions()),
                    NullLogger<UpdateRolePermissionsCommandHandler>.Instance)
                .Handle(
                    new UpdateRolePermissionsCommand(
                        targetRole.Id,
                        requestedPermissions),
                    CancellationToken.None),

            MutationKind.UpdateAndPermissions => await new UpdateRoleAndPermissionsCommandHandler(
                    db,
                    context,
                    registry,
                    NoopInvitationNotifier.Instance,
                    new TestTokenService(),
                    clock,
                    Options.Create(new IdentityModuleOptions()),
                    NullLogger<UpdateRoleAndPermissionsCommandHandler>.Instance)
                .Handle(
                    new UpdateRoleAndPermissionsCommand(
                        targetRole.Id,
                        $"Editor-{Guid.NewGuid():N}",
                        "Updated",
                        requestedPermissions),
                    CancellationToken.None),

            MutationKind.Delete => await new DeleteRoleCommandHandler(
                    db,
                    context,
                    registry,
                    clock)
                .Handle(
                    new DeleteRoleCommand(targetRole.Id),
                    CancellationToken.None),

            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-role-mutation-{Guid.NewGuid():N}")
            .Options);

    private static async Task<Seed> SeedAsync(
        IdentityDbContext db,
        IReadOnlyList<string> actorPermissions,
        IReadOnlyList<string>? targetPermissions = null)
    {
        var actorRole = Role.Create(
            $"Actor-{Guid.NewGuid():N}",
            null,
            actorPermissions,
            UserType.SystemAdministrator,
            Now).Value;
        var targetRole = Role.Create(
            $"Target-{Guid.NewGuid():N}",
            null,
            targetPermissions ?? [],
            UserType.SystemAdministrator,
            Now).Value;
        var actor = User.CreateActive(
            Email.Create($"role-actor-{Guid.NewGuid():N}@nags.sa").Value,
            "Role actor",
            actorRole.Id,
            "hash",
            Now).Value;

        db.Roles.AddRange(actorRole, targetRole);
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        return new Seed(actor, actorRole, targetRole);
    }

    private static PermissionRegistry Registry() =>
        new([new IdentityPermissionCatalog()]);

    private static TestUserContext Context(
        Guid userId,
        IReadOnlyList<string> permissions) =>
        new(userId, permissions);

    public enum MutationKind
    {
        Create,
        Update,
        UpdatePermissions,
        UpdateAndPermissions,
        Delete
    }

    private sealed record Seed(User Actor, Role ActorRole, Role TargetRole);

    private sealed class TestUserContext(
        Guid userId,
        IReadOnlyList<string> permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopInvitationNotifier : IInvitationNotifier
    {
        public static readonly NoopInvitationNotifier Instance = new();

        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestTokenService : ITokenService
    {
        public AccessToken CreateAccessToken(
            User user,
            IReadOnlyCollection<string> permissions,
            Guid sessionId) =>
            throw new NotSupportedException();

        public RefreshToken CreateRefreshToken() =>
            throw new NotSupportedException();

        public string HashRefreshToken(string rawToken) =>
            throw new NotSupportedException();

        public SecureToken CreateSecureToken() =>
            new($"invitation-{Guid.NewGuid():N}", $"hash-{Guid.NewGuid():N}");

        public string HashToken(string rawToken) =>
            throw new NotSupportedException();

        public string CreateMfaChallengeToken(User user) =>
            throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingIdentityDbContext(
        IdentityDbContext inner,
        DbUpdateException? saveException = null) : IIdentityDbContext
    {
        public int BeginCount { get; private set; }
        public int CommitCount { get; private set; }

        public DbSet<User> Users => inner.Users;
        public DbSet<Role> Roles => inner.Roles;
        public DbSet<UserSession> Sessions => inner.Sessions;
        public DbSet<OutboxMessage> OutboxMessages => inner.OutboxMessages;
        public DbSet<InboxMessage> InboxMessages => inner.InboxMessages;

        public async Task<IIdentityTransaction> BeginAccessManagementTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            BeginCount++;
            var transaction =
                await inner.BeginAccessManagementTransactionAsync(cancellationToken);
            return new TrackingTransaction(transaction, () => CommitCount++);
        }

        public Task<IIdentityTransaction> BeginSessionFamilyTransactionAsync(
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            inner.BeginSessionFamilyTransactionAsync(familyId, cancellationToken);

        public Task AcquireSessionFamilyLockAsync(
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            inner.AcquireSessionFamilyLockAsync(familyId, cancellationToken);

        public void SetOriginalRowVersion<TEntity>(
            TEntity entity,
            byte[] rowVersion)
            where TEntity : class =>
            inner.SetOriginalRowVersion(entity, rowVersion);

        public Task ReloadAsync<TEntity>(
            TEntity entity,
            CancellationToken cancellationToken = default)
            where TEntity : class =>
            inner.ReloadAsync(entity, cancellationToken);

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            saveException is null
                ? inner.SaveChangesAsync(cancellationToken)
                : Task.FromException<int>(saveException);

        private sealed class TrackingTransaction(
            IIdentityTransaction innerTransaction,
            Action onCommit) : IIdentityTransaction
        {
            public async Task CommitAsync(
                CancellationToken cancellationToken = default)
            {
                await innerTransaction.CommitAsync(cancellationToken);
                onCommit();
            }

            public ValueTask DisposeAsync() =>
                innerTransaction.DisposeAsync();
        }
    }
}
