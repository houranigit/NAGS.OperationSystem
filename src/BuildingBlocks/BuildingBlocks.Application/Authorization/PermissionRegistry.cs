using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;

namespace BuildingBlocks.Application.Authorization;

/// <summary>
/// Default <see cref="IPermissionRegistry"/> that composes all module catalogs registered in DI.
/// Registered as a singleton; the catalog is fixed at startup.
/// </summary>
public sealed class PermissionRegistry : IPermissionRegistry
{
    private readonly Dictionary<string, PermissionDescriptor> _byCode;

    public PermissionRegistry(IEnumerable<IPermissionCatalog> catalogs)
    {
        _byCode = catalogs
            .SelectMany(c => c.Permissions)
            .GroupBy(p => p.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        All = _byCode.Values.OrderBy(p => p.Code, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<PermissionDescriptor> All { get; }

    public bool IsKnown(string permission) => _byCode.ContainsKey(permission);

    public bool IsCompatibleWith(string permission, UserType userType) =>
        _byCode.TryGetValue(permission, out var descriptor) && descriptor.IsCompatibleWith(userType);

    public IReadOnlyList<string> CompatiblePermissions(UserType userType) =>
        All.Where(p => p.IsCompatibleWith(userType)).Select(p => p.Code).ToList();
}
