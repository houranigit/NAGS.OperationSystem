namespace Core.Domain.Aggregates.Customer;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(CustomerId id, CancellationToken ct = default);
    Task<Customer?> GetByIdWithContactsAsync(CustomerId id, CancellationToken ct = default);
    Task<Customer?> GetByIataCodeAsync(string iataCode, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetAllActiveAsync(CancellationToken ct = default);
    Task<bool> ExistsByIataCodeAsync(string iataCode, CancellationToken ct = default);
    Task<bool> ExistsByOfficialEmailAsync(string email, CustomerId? excludeId = null, CancellationToken ct = default);
    void Add(Customer customer);
    void Update(Customer customer);
}
