namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Persistence surface for the <see cref="Contract"/> aggregate. Implementations live in
/// <c>Contracts.Infrastructure</c>. <see cref="HasActiveOverlapAsync"/> powers the
/// no-overlap rule (#5): another non-terminal contract for the same customer that shares any
/// station ∩ any operation type ∩ overlapping period is a conflict.
/// </summary>
public interface IContractRepository
{
    /// <summary>Loads only the aggregate root + small owned VOs (no station/OT/pricing children).</summary>
    Task<Contract?> GetByIdAsync(ContractId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the aggregate with every child collection included. Use for Update / Suspend /
    /// Activate / Terminate command handlers and for the GetById query.
    /// </summary>
    Task<Contract?> GetByIdWithDetailsAsync(ContractId id, CancellationToken cancellationToken = default);

    /// <summary>True when a contract with this normalised contract number already exists.</summary>
    Task<bool> ExistsByContractNoAsync(
        string contractNo,
        ContractId? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a non-terminal (Draft / Active / Suspended) contract for
    /// <paramref name="customerId"/> shares any station from <paramref name="stationIds"/>,
    /// any operation type from <paramref name="operationTypeIds"/>, AND its period overlaps
    /// <paramref name="periodStart"/>..<paramref name="periodEnd"/>. Excludes
    /// <paramref name="excludeContractId"/> (used during Update).
    /// </summary>
    Task<bool> HasActiveOverlapAsync(
        Guid customerId,
        IReadOnlyCollection<Guid> operationTypeIds,
        IReadOnlyCollection<Guid> stationIds,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        ContractId? excludeContractId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns contracts whose stored status no longer matches the period at
    /// <paramref name="now"/>. Drives the status-sync job. Excludes Suspended and Terminated.
    /// </summary>
    Task<IReadOnlyList<Contract>> GetStatusSyncCandidatesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns Active contracts whose period entered the configured alert window at
    /// <paramref name="now"/> and have not yet been notified within the configured cadence.
    /// Drives the expiring-soon notification job.
    /// </summary>
    Task<IReadOnlyList<Contract>> GetExpiringSoonAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    void Add(Contract contract);
    void Update(Contract contract);
}
