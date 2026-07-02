using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Catalogs;

namespace OperationsSystem.Blazor.Client.Features.Catalogs.Components;

internal static class SimpleCatalogText
{
    public static string Title(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => "Services",
        SimpleCatalogKind.OperationTypes => "Operation types",
        SimpleCatalogKind.Materials => "Materials",
        SimpleCatalogKind.GeneralSupports => "General supports",
        _ => "Catalog"
    };

    public static string Description(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => "Manage operational service catalog items.",
        SimpleCatalogKind.OperationTypes => "Manage operation categories used across operations and pricing.",
        SimpleCatalogKind.Materials => "Manage material catalog items. Units can be added later.",
        SimpleCatalogKind.GeneralSupports => "Manage support catalog items. Units and duration rules can be added later.",
        _ => "Manage catalog items."
    };

    public static string Singular(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => "service",
        SimpleCatalogKind.OperationTypes => "operation type",
        SimpleCatalogKind.Materials => "material",
        SimpleCatalogKind.GeneralSupports => "general support",
        _ => "item"
    };

    public static string Route(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => "services",
        SimpleCatalogKind.OperationTypes => "operation-types",
        SimpleCatalogKind.Materials => "materials",
        SimpleCatalogKind.GeneralSupports => "general-supports",
        _ => "catalog"
    };

    public static string Icon(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => "settings",
        SimpleCatalogKind.OperationTypes => "timeline",
        SimpleCatalogKind.Materials => "category",
        SimpleCatalogKind.GeneralSupports => "headset_mic",
        _ => "category"
    };

    public static string ViewPermission(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => MasterDataPermissions.ServicesView,
        SimpleCatalogKind.OperationTypes => MasterDataPermissions.OperationTypesView,
        SimpleCatalogKind.Materials => MasterDataPermissions.MaterialsView,
        SimpleCatalogKind.GeneralSupports => MasterDataPermissions.GeneralSupportsView,
        _ => string.Empty
    };

    public static string CreatePermission(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => MasterDataPermissions.ServicesCreate,
        SimpleCatalogKind.OperationTypes => MasterDataPermissions.OperationTypesCreate,
        SimpleCatalogKind.Materials => MasterDataPermissions.MaterialsCreate,
        SimpleCatalogKind.GeneralSupports => MasterDataPermissions.GeneralSupportsCreate,
        _ => string.Empty
    };

    public static string UpdatePermission(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => MasterDataPermissions.ServicesUpdate,
        SimpleCatalogKind.OperationTypes => MasterDataPermissions.OperationTypesUpdate,
        SimpleCatalogKind.Materials => MasterDataPermissions.MaterialsUpdate,
        SimpleCatalogKind.GeneralSupports => MasterDataPermissions.GeneralSupportsUpdate,
        _ => string.Empty
    };

    public static string ActivatePermission(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => MasterDataPermissions.ServicesActivate,
        SimpleCatalogKind.OperationTypes => MasterDataPermissions.OperationTypesActivate,
        SimpleCatalogKind.Materials => MasterDataPermissions.MaterialsActivate,
        SimpleCatalogKind.GeneralSupports => MasterDataPermissions.GeneralSupportsActivate,
        _ => string.Empty
    };

    public static string DeactivatePermission(SimpleCatalogKind kind) => kind switch
    {
        SimpleCatalogKind.Services => MasterDataPermissions.ServicesDeactivate,
        SimpleCatalogKind.OperationTypes => MasterDataPermissions.OperationTypesDeactivate,
        SimpleCatalogKind.Materials => MasterDataPermissions.MaterialsDeactivate,
        SimpleCatalogKind.GeneralSupports => MasterDataPermissions.GeneralSupportsDeactivate,
        _ => string.Empty
    };
}
