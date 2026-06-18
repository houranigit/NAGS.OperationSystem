namespace Core.Domain.Aggregates.License;

public interface ILicenseRepository
{
    Task<License?> GetByIdAsync(LicenseId id, CancellationToken ct = default);
    Task<License?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<License>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<License>> GetAllActiveAsync(CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default);
    void Add(License license);
    void Update(License license);
}
