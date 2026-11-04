using System.Security.Cryptography;
using System.Text;

namespace MasterData.Contracts.Readers;

/// <summary>Customer image content for a caller that has authorized access in its owning module.</summary>
public sealed record CustomerLogoReadContent(byte[] Content, string ContentType, string Version);

public interface ICustomerLogoReader
{
    public Task<CustomerLogoReadContent?> GetLogoAsync(Guid customerId, CancellationToken cancellationToken);
    public Task<IReadOnlyDictionary<Guid, string>> GetLogoVersionsAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken);
}

/// <summary>Opaque version metadata computed by MasterData from its own persisted logo reference.</summary>
public static class CustomerLogoVersions
{
    public const string None = "none";

    public static string FromReference(string? reference) =>
        string.IsNullOrWhiteSpace(reference)
            ? None
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reference)));
}
