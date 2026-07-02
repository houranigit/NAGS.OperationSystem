using Microsoft.Extensions.Options;

namespace OperationsSystem.Blazor.Api;

public sealed class ApiProxyOptions
{
    public const string SectionName = "ApiProxy";

    public string BaseUrl { get; init; } = "http://localhost:5211";
}

public sealed class ApiProxyOptionsValidator : IValidateOptions<ApiProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiProxyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return ValidateOptionsResult.Fail("ApiProxy:BaseUrl is required.");

        if (!Uri.TryCreate(options.BaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return ValidateOptionsResult.Fail("ApiProxy:BaseUrl must be an absolute URI.");

        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return ValidateOptionsResult.Fail("ApiProxy:BaseUrl must use http or https.");

        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            return ValidateOptionsResult.Fail("ApiProxy:BaseUrl must not include a query string or fragment.");

        if (!string.IsNullOrEmpty(baseUri.AbsolutePath) && baseUri.AbsolutePath != "/")
            return ValidateOptionsResult.Fail("ApiProxy:BaseUrl must point to the API origin, not an API path.");

        return ValidateOptionsResult.Success;
    }
}
