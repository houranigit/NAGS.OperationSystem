using System.Security.Cryptography;
using System.Text;

namespace MasterData.Infrastructure.Seeding;

/// <summary>Derives stable, never-regenerated ids for seeded MasterData rows from a natural key.</summary>
public static class MasterDataSeedIds
{
    /// <summary>Deterministic GUID for a seeded record, derived from a kind and natural key.</summary>
    public static Guid For(string kind, string key)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"masterdata:{kind}:{key}"));
        return new Guid(bytes);
    }
}
