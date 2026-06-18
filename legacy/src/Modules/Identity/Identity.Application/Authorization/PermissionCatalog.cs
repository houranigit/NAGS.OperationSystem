using System.Reflection;
using Identity.Domain.Authorization;

namespace Identity.Application.Authorization;

/// <summary>Flat list of permission codes defined in <see cref="Permissions"/> for admin UIs.</summary>
public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionItem> All { get; } = Build();

    private static IReadOnlyList<PermissionItem> Build()
    {
        var list = new List<PermissionItem>();
        foreach (var nested in typeof(Permissions).GetNestedTypes(BindingFlags.Public))
        {
            foreach (var field in nested.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.FieldType != typeof(string))
                    continue;

                var raw = field.GetRawConstantValue();
                if (raw is not string code)
                    continue;

                list.Add(new PermissionItem(nested.Name, field.Name, code));
            }
        }

        return list
            .OrderBy(p => p.Group, StringComparer.Ordinal)
            .ThenBy(p => p.FieldName, StringComparer.Ordinal)
            .ToList();
    }
}

public readonly record struct PermissionItem(string Group, string FieldName, string Code);
