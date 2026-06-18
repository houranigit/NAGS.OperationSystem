using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Reflection;

namespace Host.Web.Configuration;

/// <summary>
/// Lets <see cref="System.Linq.Dynamic.Core"/> resolve enum types coming from
/// Radzen DataGrid filter expressions. The Radzen grid serializes an enum filter as
/// the value's fully qualified type name (e.g. <c>Operations.Domain.Enumerations.FlightStatus.Scheduled</c>);
/// the default Dynamic.Core type provider only knows types decorated with
/// <c>[DynamicLinqType]</c>, so without this provider every enum-column filter throws
/// <c>"Type 'X.Y.Z' not found"</c>.
/// </summary>
/// <remarks>
/// Configured once at startup via <c>ParsingConfig.Default.CustomTypeProvider</c>; every
/// <c>query.Where(filterString)</c> / <c>query.OrderBy(orderString)</c> in every paginated
/// query handler then resolves these enums automatically — no per-handler change needed.
/// </remarks>
internal sealed class DynamicLinqEnumTypeProvider : IDynamicLinqCustomTypeProvider
{
    private readonly HashSet<Type> _types;
    private readonly Dictionary<string, Type> _byFullName;
    private readonly Dictionary<string, Type> _bySimpleName;

    public DynamicLinqEnumTypeProvider(params Assembly[] assemblies)
    {
        _types = assemblies
            .SelectMany(SafeGetExportedTypes)
            .Where(t => t.IsEnum)
            .ToHashSet();

        _byFullName = _types
            .Where(t => t.FullName is not null)
            .GroupBy(t => t.FullName!)
            .ToDictionary(g => g.Key, g => g.First());

        _bySimpleName = _types
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public HashSet<Type> GetCustomTypes() => _types;

    public Dictionary<Type, List<MethodInfo>> GetExtensionMethods() => new();

    public Type ResolveType(string typeName) =>
        _byFullName.TryGetValue(typeName, out var byFull) ? byFull
        : _bySimpleName.TryGetValue(typeName, out var bySimple) ? bySimple
        : null!;

    public Type ResolveTypeBySimpleName(string simpleTypeName) =>
        _bySimpleName.TryGetValue(simpleTypeName, out var t) ? t : null!;

    private static IEnumerable<Type> SafeGetExportedTypes(Assembly a)
    {
        try { return a.GetExportedTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
