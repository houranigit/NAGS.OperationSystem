using Core.Domain.Aggregates.License;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class LicenseRepository(CoreDbContext context) : ILicenseRepository
{
    public async Task<License?> GetByIdAsync(LicenseId id, CancellationToken ct = default) =>
        await context.Licenses.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<License?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await context.Licenses.FirstOrDefaultAsync(x => x.Code == normalized, ct);
    }

    public async Task<IReadOnlyList<License>> GetAllAsync(CancellationToken ct = default) =>
        await context.Licenses.ToListAsync(ct);

    public async Task<IReadOnlyList<License>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.Licenses.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await context.Licenses.AnyAsync(x => x.Code == normalized, ct);
    }

    public void Add(License license) => context.Licenses.Add(license);
    public void Update(License license) => context.Licenses.Update(license);
}
