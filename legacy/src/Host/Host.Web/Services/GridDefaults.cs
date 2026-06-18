using Radzen;

namespace Host.Web.Services;

/// <summary>
/// Centralized default settings for all Radzen DataGrids in the application.
/// </summary>
public static class GridDefaults
{
    public static FilterMode FilterMode => FilterMode.Simple;
    public static LogicalFilterOperator LogicalFilterOperator => LogicalFilterOperator.And;
    public static FilterCaseSensitivity FilterCaseSensitivity => FilterCaseSensitivity.CaseInsensitive;
    public static Density Density => Density.Compact;
    public static int DefaultPageSize => 20;
    public static int VirtualizationOverscanCount => 20;
    public static bool AllowPaging => false;
    public static bool AllowSorting => true;
    public static bool AllowFiltering => true;
    public static bool AllowColumnResize => true;
    public static bool AllowColumnPicking => true;
    public static bool AllowColumnReorder => true;
    public static bool AllowAlternatingRows => true;
    public static bool AllowVirtualization => true;
    public static bool AllowRowSelectOnRowClick => true;

    public static IEnumerable<int> PageSizeOptions => [10, 15, 25, 50, 100];
}
