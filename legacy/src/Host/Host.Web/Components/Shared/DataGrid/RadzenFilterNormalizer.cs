using System.Text.RegularExpressions;

namespace Host.Web.Components.Shared.DataGrid;

/// <summary>
/// Cleans up the Dynamic LINQ filter / order-by strings Radzen DataGrid emits so that
/// <c>System.Linq.Dynamic.Core</c> can parse them and so they line up with the entity
/// shape the server-side handler executes <c>query.Where</c> on.
/// </summary>
/// <remarks>
/// Two transformations:
/// <list type="number">
/// <item><description><b>Strip enum casts.</b> Radzen serializes an enum constant as a C-style cast
/// (see <c>ExpressionSerializer.FormatValue</c>): <c>(Operations.Domain.Enumerations.FlightStatus)1</c>.
/// Dynamic.Core 1.7.x has no C-style cast handler — it routes <c>(Foo.Bar.Baz)1</c> through
/// <c>ParseAsEnumOrNestedClass</c> and throws <c>"Type 'Foo.Bar' not found"</c>. Stripping the cast
/// is safe because <c>x.Status == 1</c> parses fine — the parser converts the enum operand to its
/// underlying numeric type for the equality comparison.</description></item>
/// <item><description><b>Remap property names.</b> Radzen builds the filter lambda client-side
/// against <c>TItem</c> (the DTO), so the serialized string uses DTO property names. When the
/// server-side handler runs <c>IQueryable&lt;TEntity&gt;.Where(filter)</c>, those names need to be
/// rewritten to the entity's property paths (e.g. <c>FlightDto.CustomerSnapshot</c> →
/// <c>Flight.Customer</c>). The remap is a whole-identifier swap — partial matches like
/// <c>SomeCustomerSnapshotPrefix</c> are left alone.</description></item>
/// </list>
/// </remarks>
internal static partial class RadzenFilterNormalizer
{
    [GeneratedRegex(@"\(\s*[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)+\s*\)(?=-?\d)", RegexOptions.Compiled)]
    private static partial Regex EnumCastRegex();

    public static string? StripEnumCasts(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return filter;

        return EnumCastRegex().Replace(filter, string.Empty);
    }

    /// <summary>
    /// Replace every whole-identifier occurrence of a key in <paramref name="propertyMap"/>
    /// with its mapped value. Used to translate DTO-shaped filter / order-by strings into the
    /// entity shape on the server (e.g. <c>CustomerSnapshot.Name</c> → <c>Customer.Name</c>).
    /// </summary>
    public static string? RemapProperties(string? input, IReadOnlyDictionary<string, string>? propertyMap)
    {
        if (string.IsNullOrWhiteSpace(input) || propertyMap is null || propertyMap.Count == 0)
            return input;

        // One pass per mapping with \b boundaries so we don't accidentally rewrite substrings.
        var output = input;
        foreach (var (from, to) in propertyMap)
            output = Regex.Replace(output, $@"\b{Regex.Escape(from)}\b", to);
        return output;
    }
}
