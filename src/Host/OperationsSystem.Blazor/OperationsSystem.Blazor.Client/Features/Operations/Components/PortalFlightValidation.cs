namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed class PortalFlightValidation
{
    private readonly List<Issue> issues = [];

    internal int Step { private get; set; }
    internal int FirstInvalidStep => issues.Min(issue => issue.Step);
    public int Count => issues.Count;
    public IEnumerable<string> Messages => issues.OrderBy(issue => issue.Step).Select(issue => issue.Message).Distinct();

    public static string Field(Guid id, string name) => $"{id:N}-{name}";

    internal static string ErrorId(string field) => $"portal-field-error-{field}";

    public bool HasError(string? field) => field is not null && issues.Any(issue => issue.Field == field);

    public bool HasErrorFor(Guid id)
    {
        var prefix = $"{id:N}-";
        return issues.Any(issue => issue.Field.StartsWith(prefix, StringComparison.Ordinal));
    }

    public string? DescriptionId(string? field) => field is not null && HasError(field) ? ErrorId(field) : null;

    public string? InvalidAttribute(string? field) => HasError(field) ? "true" : null;

    internal Dictionary<string, object>? InputAttributes(string? field) => DescriptionId(field) is { } descriptionId
        ? new() { ["aria-invalid"] = "true", ["aria-describedby"] = descriptionId }
        : null;

    internal IEnumerable<string> For(string? field) => issues.Where(issue => issue.Field == field).Select(issue => issue.Message).Distinct();

    internal void Add(string field, string message) => issues.Add(new(Step, field, message));

    internal bool HasErrorsInStep(int step) => issues.Any(issue => issue.Step == step);

    internal void ClearStep(int step) => issues.RemoveAll(issue => issue.Step == step);

    private sealed record Issue(int Step, string Field, string Message);
}
