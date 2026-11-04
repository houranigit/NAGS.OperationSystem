using BuildingBlocks.Application.Abstractions;
using MasterData.Contracts.Readers;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Infrastructure.Readers;

/// <summary>Resolves customer logo content without exposing the owner's file-storage reference.</summary>
public sealed class CustomerLogoReader(MasterDataDbContext db, IFileStorage storage) : ICustomerLogoReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetLogoVersionsAsync(
        IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
            return new Dictionary<Guid, string>();

        var references = await db.Customers.AsNoTracking()
            .Where(customer => customerIds.Contains(customer.Id))
            .Select(customer => new { customer.Id, customer.LogoFileReference })
            .ToListAsync(cancellationToken);
        return references.ToDictionary(customer => customer.Id,
            customer => CustomerLogoVersions.FromReference(customer.LogoFileReference));
    }

    public async Task<CustomerLogoReadContent?> GetLogoAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var reference = await db.Customers.AsNoTracking()
            .Where(customer => customer.Id == customerId)
            .Select(customer => customer.LogoFileReference)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        await using var stream = await storage.OpenAsync(reference, cancellationToken);
        if (stream is null)
            return null;

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var content = memory.ToArray();
        return ContentType(content) is { } contentType
            ? new CustomerLogoReadContent(content, contentType, CustomerLogoVersions.FromReference(reference))
            : null;
    }

    // Uploads accept raster images independently of the original filename extension.
    private static string? ContentType(byte[] content)
    {
        if (content.Length >= 8 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E
            && content[3] == 0x47 && content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A)
            return "image/png";
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            return "image/jpeg";
        if (content.Length >= 6 && content[0] == 0x47 && content[1] == 0x49 && content[2] == 0x46
            && content[3] == 0x38 && content[4] is 0x37 or 0x39 && content[5] == 0x61)
            return "image/gif";
        if (content.Length >= 12 && content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46
            && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50)
            return "image/webp";
        return null;
    }
}
