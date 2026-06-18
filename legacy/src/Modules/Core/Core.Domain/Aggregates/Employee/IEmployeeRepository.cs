using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.Station;

namespace Core.Domain.Aggregates.Employee;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(EmployeeId id, CancellationToken ct = default);

    Task<Employee?> GetByIdWithLicensesAsync(EmployeeId id, CancellationToken ct = default);
    Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Employee>> GetAllActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Employee>> GetByStationAsync(StationId stationId, CancellationToken ct = default);
    Task<IReadOnlyList<Employee>> GetByManpowerTypeAsync(ManpowerTypeId manpowerTypeId, CancellationToken ct = default);
    Task<Employee?> GetByLinkedUserIdAsync(Guid linkedUserId, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, EmployeeId? excludeId = null, CancellationToken ct = default);
    void Add(Employee employee);
    void Update(Employee employee);
}
