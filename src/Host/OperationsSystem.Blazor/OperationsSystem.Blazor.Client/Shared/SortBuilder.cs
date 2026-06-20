using Radzen;

namespace OperationsSystem.Blazor.Client.Shared;

/// <summary>
/// Translates a Radzen <see cref="LoadDataArgs"/> sort into the API's <c>field:direction</c>
/// query form (e.g. <c>displayName:asc</c>). v1.0.0 grids sort by a single column, so only the
/// first sort descriptor is used. The column's <c>Property</c> becomes the field name; the
/// backend matches it case-insensitively against its whitelist of sortable columns.
/// </summary>
public static class SortBuilder
{
    public static string? From(LoadDataArgs args)
    {
        var sort = args.Sorts?.FirstOrDefault();
        if (sort is null || string.IsNullOrWhiteSpace(sort.Property))
            return null;

        var direction = sort.SortOrder == SortOrder.Descending ? "desc" : "asc";
        return $"{sort.Property}:{direction}";
    }
}
