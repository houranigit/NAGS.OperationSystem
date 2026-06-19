namespace OperationsSystem.Blazor.Api;

public sealed class ApiProxyOptions
{
    public const string SectionName = "ApiProxy";

    public string BaseUrl { get; init; } = "http://localhost:5211";
}
