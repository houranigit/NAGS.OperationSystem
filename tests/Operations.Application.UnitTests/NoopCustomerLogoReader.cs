using MasterData.Contracts.Readers;

namespace Operations.Application.UnitTests;

internal sealed class NoopCustomerLogoReader : ICustomerLogoReader
{
    public static readonly NoopCustomerLogoReader Instance = new();

    public Task<CustomerLogoReadContent?> GetLogoAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerLogoReadContent?>(null);

    public Task<IReadOnlyDictionary<Guid, string>> GetLogoVersionsAsync(IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(customerIds.ToDictionary(id => id, _ => CustomerLogoVersions.None));
}
