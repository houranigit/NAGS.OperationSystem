namespace MasterData.Contracts.Readers;

/// <summary>Customer image content for a caller that has authorized access in its owning module.</summary>
public sealed record CustomerLogoReadContent(byte[] Content, string ContentType);

public interface ICustomerLogoReader
{
    public Task<CustomerLogoReadContent?> GetLogoAsync(Guid customerId, CancellationToken cancellationToken);
}
