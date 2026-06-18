using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.Station;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class EmployeeRepository(CoreDbContext context) : IEmployeeRepository
{
    public async Task<Employee?> GetByIdAsync(EmployeeId id, CancellationToken ct = default) =>
        await context.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Employee?> GetByIdWithLicensesAsync(EmployeeId id, CancellationToken ct = default) =>
        await context.Employees
            .Include(x => x.Licenses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken ct = default) =>
        await context.Employees.ToListAsync(ct);

    public async Task<IReadOnlyList<Employee>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.Employees.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<IReadOnlyList<Employee>> GetByStationAsync(StationId stationId, CancellationToken ct = default) =>
        await context.Employees.Where(x => x.StationId == stationId).ToListAsync(ct);

    public async Task<IReadOnlyList<Employee>> GetByManpowerTypeAsync(ManpowerTypeId manpowerTypeId, CancellationToken ct = default) =>
        await context.Employees.Where(x => x.ManpowerTypeId == manpowerTypeId).ToListAsync(ct);

    public async Task<Employee?> GetByLinkedUserIdAsync(Guid linkedUserId, CancellationToken ct = default) =>
        await context.Employees.FirstOrDefaultAsync(x => x.LinkedUserId == linkedUserId, ct);

    public async Task<bool> ExistsByEmailAsync(string email, EmployeeId? excludeId = null, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await context.Employees.AnyAsync(
            x => x.Email == normalized && (excludeId == null || x.Id != excludeId),
            ct);
    }

    public void Add(Employee employee) => context.Employees.Add(employee);
    public void Update(Employee employee) => context.Employees.Update(employee);
}
