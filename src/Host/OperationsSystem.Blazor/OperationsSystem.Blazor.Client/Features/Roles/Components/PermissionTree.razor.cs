using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Localization;

namespace OperationsSystem.Blazor.Client.Features.Roles.Components;

public partial class PermissionTree
{
    [Parameter, EditorRequired] public IReadOnlyList<PermissionGroup> Catalog { get; set; } = [];
    [Parameter] public IReadOnlyList<string> SelectedPermissions { get; set; } = [];
    [Parameter] public EventCallback<IReadOnlyList<string>> SelectedPermissionsChanged { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool IsLoading { get; set; }
    [Parameter] public bool Compact { get; set; }

    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private readonly HashSet<string> expandedModules = new(StringComparer.Ordinal);
    private readonly HashSet<string> expandedResources = new(StringComparer.Ordinal);
    private IReadOnlyList<ModuleNode> modules = [];
    private string catalogSignature = string.Empty;
    private string searchTerm = string.Empty;

    private string RootClass => $"pt-root{(Compact ? " pt-root--compact" : string.Empty)}{(Disabled ? " pt-root--disabled" : string.Empty)}";
    private int PermissionCount => modules.Sum(module => module.Permissions.Count);
    private int SelectedKnownCount => modules.SelectMany(module => module.Permissions).Count(selected.Contains);
    private int SelectionPercentage => PermissionCount == 0 ? 0 : (int)Math.Round(SelectedKnownCount * 100d / PermissionCount);
    private bool IsFiltering => !string.IsNullOrWhiteSpace(searchTerm);

    private IReadOnlyList<ModuleNode> VisibleModules => FilterModules();

    protected override void OnParametersSet()
    {
        var incoming = SelectedPermissions.ToHashSet(StringComparer.Ordinal);
        var nextSignature = string.Join('|', Catalog.SelectMany(group => group.Permissions).OrderBy(code => code, StringComparer.Ordinal));
        if (!string.Equals(catalogSignature, nextSignature, StringComparison.Ordinal))
        {
            catalogSignature = nextSignature;
            modules = BuildModules(Catalog);
            expandedModules.Clear();
            expandedResources.Clear();
            ExpandInitialPath(incoming);
        }

        if (!selected.SetEquals(incoming))
        {
            selected.Clear();
            selected.UnionWith(incoming);
        }
    }

    private IReadOnlyList<ModuleNode> FilterModules()
    {
        var query = searchTerm.Trim();
        if (query.Length == 0)
            return modules;

        var filtered = new List<ModuleNode>();
        foreach (var module in modules)
        {
            var moduleMatch = Matches(module.Key, query) || Matches(module.Label, query);
            var resources = new List<ResourceNode>();
            foreach (var resource in module.Resources)
            {
                var resourceMatch = moduleMatch || Matches(resource.Key, query) || Matches(resource.Label, query);
                var permissions = resourceMatch
                    ? resource.Permissions
                    : resource.Permissions.Where(permission =>
                        Matches(permission, query) || Matches(ActionLabel(permission), query)).ToList();

                if (permissions.Count > 0)
                    resources.Add(resource with { Permissions = permissions });
            }

            if (resources.Count > 0)
                filtered.Add(module with
                {
                    Resources = resources,
                    Permissions = resources.SelectMany(resource => resource.Permissions).ToList()
                });
        }

        return filtered;
    }

    private bool IsModuleExpanded(string key) => IsFiltering || expandedModules.Contains(key);
    private bool IsResourceExpanded(string key) => IsFiltering || expandedResources.Contains(key);

    private void ExpandInitialPath(HashSet<string> incoming)
    {
        var module = modules.FirstOrDefault(candidate => candidate.Permissions.Any(incoming.Contains))
                     ?? modules.FirstOrDefault();
        if (module is null)
            return;

        expandedModules.Add(module.Key);
        var resource = module.Resources.FirstOrDefault(candidate => candidate.Permissions.Any(incoming.Contains))
                       ?? module.Resources.FirstOrDefault();
        if (resource is not null)
            expandedResources.Add(resource.Key);
    }

    private void ToggleModule(string key)
    {
        if (IsFiltering)
            return;

        if (!expandedModules.Add(key))
            expandedModules.Remove(key);
    }

    private void ToggleResource(string key)
    {
        if (IsFiltering)
            return;

        if (!expandedResources.Add(key))
            expandedResources.Remove(key);
    }

    private void ExpandAll()
    {
        expandedModules.UnionWith(modules.Select(module => module.Key));
        expandedResources.UnionWith(modules.SelectMany(module => module.Resources).Select(resource => resource.Key));
    }

    private void CollapseAll()
    {
        expandedModules.Clear();
        expandedResources.Clear();
    }

    private void ClearSearch() => searchTerm = string.Empty;

    private async Task SelectAllAsync()
    {
        if (Disabled)
            return;

        selected.UnionWith(modules.SelectMany(module => module.Permissions));
        await NotifySelectionChangedAsync();
    }

    private async Task ClearAllAsync()
    {
        if (Disabled)
            return;

        selected.ExceptWith(modules.SelectMany(module => module.Permissions));
        await NotifySelectionChangedAsync();
    }

    private async Task ToggleGroupAsync(IReadOnlyList<string> permissions)
    {
        if (Disabled)
            return;

        if (permissions.All(selected.Contains))
            selected.ExceptWith(permissions);
        else
            selected.UnionWith(permissions);

        await NotifySelectionChangedAsync();
    }

    private async Task TogglePermissionAsync(string permission)
    {
        if (Disabled)
            return;

        if (!selected.Add(permission))
            selected.Remove(permission);

        await NotifySelectionChangedAsync();
    }

    private Task NotifySelectionChangedAsync() =>
        SelectedPermissionsChanged.InvokeAsync((IReadOnlyList<string>)selected.OrderBy(code => code, StringComparer.Ordinal).ToList());

    private int SelectedCount(IReadOnlyList<string> permissions) => permissions.Count(selected.Contains);

    private SelectionState State(IReadOnlyList<string> permissions)
    {
        var count = SelectedCount(permissions);
        return count switch
        {
            0 => SelectionState.None,
            _ when count == permissions.Count => SelectionState.All,
            _ => SelectionState.Partial
        };
    }

    private string CheckboxClass(IReadOnlyList<string> permissions) => $"pt-checkbox pt-checkbox--{State(permissions).ToString().ToLowerInvariant()}";
    private string PermissionClass(string permission) => $"pt-permission{(selected.Contains(permission) ? " pt-permission--selected" : string.Empty)}";
    private string ModuleClass(ModuleNode module) => $"pt-module{(State(module.Permissions) != SelectionState.None ? " pt-module--has-selection" : string.Empty)}";
    private string ResourceClass(ResourceNode resource) => $"pt-resource{(State(resource.Permissions) != SelectionState.None ? " pt-resource--has-selection" : string.Empty)}";

    private string SelectionAria(IReadOnlyList<string> permissions) => State(permissions) switch
    {
        SelectionState.All => "true",
        SelectionState.Partial => "mixed",
        _ => "false"
    };

    private string ToggleTitle(string label, IReadOnlyList<string> permissions) =>
        State(permissions) == SelectionState.All
            ? string.Format(UiStrings.Roles.ClearGroup, label)
            : string.Format(UiStrings.Roles.SelectGroup, label);

    private static bool Matches(string value, string query) => value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ModuleNode> BuildModules(IReadOnlyList<PermissionGroup> catalog) => catalog
        .GroupBy(group => ModuleKey(group.Resource), StringComparer.Ordinal)
        .Select(group =>
        {
            var resources = group
                .Select(item => new ResourceNode(
                    item.Resource,
                    ResourceLabel(ResourceKey(item.Resource)),
                    ResourceIcon(ResourceKey(item.Resource)),
                    item.Permissions.OrderBy(permission => permission, StringComparer.Ordinal).ToList()))
                .OrderBy(resource => resource.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ModuleNode(
                group.Key,
                ModuleLabel(group.Key),
                ModuleIcon(group.Key),
                resources,
                resources.SelectMany(resource => resource.Permissions).ToList());
        })
        .OrderBy(module => ModuleOrder(module.Key))
        .ThenBy(module => module.Label, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string ModuleKey(string resource) => resource.Split('.', 2)[0];
    private static string ResourceKey(string resource) => resource.Split('.', 2) is { Length: 2 } parts ? parts[1] : resource;

    private static string ModuleLabel(string key) => key switch
    {
        "identity" => UiStrings.Roles.ModuleIdentity,
        "operations" => UiStrings.Roles.ModuleOperations,
        "masterdata" => UiStrings.Roles.ModuleMasterData,
        "audit" => UiStrings.Roles.ModuleAudit,
        "notifications" => UiStrings.Roles.ModuleNotifications,
        _ => Humanize(key)
    };

    private static string ModuleIcon(string key) => key switch
    {
        "identity" => "admin_panel_settings",
        "operations" => "flight_takeoff",
        "masterdata" => "database",
        "audit" => "policy",
        "notifications" => "notifications",
        _ => "extension"
    };

    private static int ModuleOrder(string key) => key switch
    {
        "identity" => 0,
        "operations" => 1,
        "masterdata" => 2,
        "audit" => 3,
        _ => 10
    };

    private static string ResourceIcon(string key) => key switch
    {
        "users" => "group",
        "roles" => "shield",
        "sessions" => "devices",
        "dashboard" => "dashboard",
        "flights" => "flight",
        "work-orders" => "assignment",
        "staff-allocation" => "hub",
        "staff-members" => "groups",
        "stations" => "flight_takeoff",
        "countries" => "public",
        "customers" => "business",
        "customer-contacts" => "contact_page",
        "aircraft-types" => "airplanemode_active",
        "manpower-types" => "engineering",
        "licenses" => "workspace_premium",
        "services" => "room_service",
        "operation-types" => "category",
        "tools" => "construction",
        "materials" => "inventory_2",
        "general-supports" => "support_agent",
        "reference" => "list_alt",
        "trails" => "history",
        _ => "folder"
    };

    private static string ActionLabel(string permission)
    {
        var action = permission[(permission.LastIndexOf('.') + 1)..];
        return action switch
        {
            "reset-mfa" => UiStrings.Roles.ActionResetMfa,
            "view-options" => UiStrings.Roles.ActionViewOptions,
            "view-station" => UiStrings.Roles.ActionViewStation,
            "view-others" => UiStrings.Roles.ActionViewOthers,
            "manage-others" => UiStrings.Roles.ActionManageOthers,
            "delete-others" => UiStrings.Roles.ActionDeleteOthers,
            "assign-role" => UiStrings.Roles.ActionAssignRole,
            "manage-permissions" => UiStrings.Roles.ActionManagePermissions,
            "grant-access" => UiStrings.Roles.ActionGrantAccess,
            "restore-access" => UiStrings.Roles.ActionRestoreAccess,
            _ => UiStrings.Roles.ActionLabel(action, Humanize(action))
        };
    }

    private static string ResourceLabel(string resource) =>
        UiStrings.Roles.ResourceLabel(resource, Humanize(resource));

    private static string Humanize(string value)
    {
        var words = value.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', words.Select(word => word.ToUpperInvariant() switch
        {
            "MFA" => "MFA",
            "ID" => "ID",
            "API" => "API",
            _ => char.ToUpperInvariant(word[0]) + word[1..]
        }));
    }

    private enum SelectionState { None, Partial, All }

    private sealed record ModuleNode(
        string Key,
        string Label,
        string Icon,
        IReadOnlyList<ResourceNode> Resources,
        IReadOnlyList<string> Permissions);

    private sealed record ResourceNode(
        string Key,
        string Label,
        string Icon,
        IReadOnlyList<string> Permissions);
}
