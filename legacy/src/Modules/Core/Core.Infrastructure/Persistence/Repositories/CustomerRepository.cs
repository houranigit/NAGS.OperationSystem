using Core.Domain.Aggregates.Customer;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class CustomerRepository(CoreDbContext context) : ICustomerRepository
{
    public async Task<Customer?> GetByIdAsync(CustomerId id, CancellationToken ct = default) =>
        await context.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Customer?> GetByIdWithContactsAsync(CustomerId id, CancellationToken ct = default) =>
        await context.Customers
            .Include(x => x.Contacts)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Customer?> GetByIataCodeAsync(string iataCode, CancellationToken ct = default)
    {
        var parsed = Core.Domain.ValueObjects.IataAirlineCode.Create(iataCode);
        if (parsed.IsFailure) return null;
        var code = parsed.Value.Value;
        return await context.Customers.FirstOrDefaultAsync(x => x.IataCode.Value == code, ct);
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default) =>
        await context.Customers.ToListAsync(ct);

    public async Task<IReadOnlyList<Customer>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.Customers.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<bool> ExistsByIataCodeAsync(string iataCode, CancellationToken ct = default)
    {
        var parsed = Core.Domain.ValueObjects.IataAirlineCode.Create(iataCode);
        if (parsed.IsFailure) return false;
        var code = parsed.Value.Value;
        return await context.Customers.AnyAsync(x => x.IataCode.Value == code, ct);
    }

    public async Task<bool> ExistsByOfficialEmailAsync(string email, CustomerId? excludeId = null, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await context.Customers.AnyAsync(
            x => x.OfficialEmail == normalized && (excludeId == null || x.Id != excludeId),
            ct);
    }

    public void Add(Customer customer) => context.Customers.Add(customer);
    public void Update(Customer customer) => context.Customers.Update(customer);
}
