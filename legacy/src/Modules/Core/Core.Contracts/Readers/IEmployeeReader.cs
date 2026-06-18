using Core.Contracts.Features.Employee;

namespace Core.Contracts.Readers;

public interface IEmployeeReader
{
    Task<EmployeeSnapshot?> GetByIdAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Type-ahead search on full name (contains, case-insensitive). Active employees only.</summary>
    Task<IReadOnlyList<EmployeeSearchResultDto>> SearchActiveByNameAsync(
        string search,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the employee whose <c>LinkedUserId</c> matches <paramref name="userId"/>.
    /// Used by the mobile API to map "JWT user" → "employee identity" for every request.
    /// </summary>
    Task<EmployeeSnapshot?> GetByLinkedUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverse projection used by Notifications: given an <paramref name="employeeId"/>,
    /// returns the Identity user id that employee is linked to (or <c>null</c> if not
    /// yet linked). Avoids materialising the whole employee row when only the user id
    /// is needed.
    /// </summary>
    Task<Guid?> GetLinkedUserIdByEmployeeIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Type-ahead search restricted to active employees at a given station, used by the
    /// mobile invite-teammate screen so users only see colleagues at their own station.
    /// </summary>
    Task<IReadOnlyList<EmployeeSearchResultDto>> SearchActiveByStationAsync(
        Guid stationId,
        string? search,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same shape as <see cref="SearchActiveByStationAsync"/> but returns the richer
    /// <see cref="EmployeeSnapshot"/> (with station + manpower-type details) so the
    /// mobile invite-teammate UI can render the role / station chips on each row.
    /// </summary>
    Task<IReadOnlyList<EmployeeSnapshot>> SearchActiveSnapshotsByStationAsync(
        Guid stationId,
        string? search,
        int take,
        CancellationToken cancellationToken = default);
}
