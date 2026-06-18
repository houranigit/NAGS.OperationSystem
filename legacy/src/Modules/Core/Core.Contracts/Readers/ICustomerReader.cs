using Core.Contracts.Features.Customer;

namespace Core.Contracts.Readers;

/// <summary>Cross-module read surface for Customer lean snapshots (<see cref="CustomerSnapshot"/>).</summary>
public interface ICustomerReader
{
    Task<CustomerSnapshot?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> customerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All active customers — used by the mobile bootstrap to populate the
    /// "Create work order from scratch" customer picker fully offline.
    /// </summary>
    Task<IReadOnlyList<CustomerSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>True when a customer with this id exists AND is active. Used by other modules
    /// to enforce active-state without bloating the lean snapshot DTO.</summary>
    Task<bool> ExistsActiveAsync(Guid customerId, CancellationToken cancellationToken = default);
}
