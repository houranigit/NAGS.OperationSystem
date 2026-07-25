using System.Data;
using System.Reflection;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Messaging;
using Identity.Application.Abstractions;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options), IIdentityDbContext, IOutboxDbContext
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public async Task<IIdentityTransaction> BeginAccessManagementTransactionAsync(
        CancellationToken cancellationToken = default) =>
        await BeginTransactionWithApplicationLockAsync(
            "Identity.AccessManagement",
            IsolationLevel.Serializable,
            cancellationToken);

    public async Task<IIdentityTransaction> BeginSessionFamilyTransactionAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        if (familyId == Guid.Empty)
            throw new DbUpdateConcurrencyException("Cannot lock an empty session family.");

        // The application lock is the serialization boundary for one token family. ReadCommitted
        // deliberately releases ordinary read locks after each query, preventing refresh from
        // holding user/session S-locks while an access-management transaction updates those rows.
        return await BeginTransactionWithApplicationLockAsync(
            $"Identity.SessionFamily.{familyId:N}",
            IsolationLevel.ReadCommitted,
            cancellationToken);
    }

    public async Task AcquireSessionFamilyLockAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        if (familyId == Guid.Empty)
            throw new DbUpdateConcurrencyException("Cannot lock an empty session family.");

        if (!Database.IsRelational() || !Database.IsSqlServer())
            return;

        var transaction = Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "A session-family lock requires an active database transaction.");
        await AcquireApplicationLockAsync(
            transaction,
            $"Identity.SessionFamily.{familyId:N}",
            cancellationToken);
    }

    private async Task<IIdentityTransaction> BeginTransactionWithApplicationLockAsync(
        string lockResource,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        if (!Database.IsRelational())
            return NoOpIdentityTransaction.Instance;

        var transaction = await Database.BeginTransactionAsync(
            isolationLevel,
            cancellationToken);

        try
        {
            if (Database.IsSqlServer())
                await AcquireApplicationLockAsync(
                    transaction,
                    lockResource,
                    cancellationToken);

            return new IdentityTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
        Entry(entity).Property(nameof(User.RowVersion)).OriginalValue = rowVersion;

    public Task ReloadAsync<TEntity>(
        TEntity entity,
        CancellationToken cancellationToken = default)
        where TEntity : class =>
        Entry(entity).ReloadAsync(cancellationToken);

    private async Task AcquireApplicationLockAsync(
        IDbContextTransaction transaction,
        string lockResource,
        CancellationToken cancellationToken)
    {
        await using var command = Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 10000;
            SELECT @result;
            """;
        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.Value = lockResource;
        command.Parameters.Add(resourceParameter);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        if (scalar is not int result || result < 0)
        {
            throw new DbUpdateConcurrencyException(
                $"Could not acquire the Identity application lock '{lockResource}'.");
        }
    }

    private sealed class IdentityTransaction(IDbContextTransaction transaction) : IIdentityTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class NoOpIdentityTransaction : IIdentityTransaction
    {
        public static readonly NoOpIdentityTransaction Instance = new();

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyOutboxInbox();
        base.OnModelCreating(modelBuilder);
    }
}
