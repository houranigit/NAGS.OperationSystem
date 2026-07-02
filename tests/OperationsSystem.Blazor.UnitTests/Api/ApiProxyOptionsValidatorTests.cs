using Microsoft.Extensions.Options;
using OperationsSystem.Blazor.Api;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Api;

public sealed class ApiProxyOptionsValidatorTests
{
    private readonly ApiProxyOptionsValidator validator = new();

    [Theory]
    [InlineData("http://localhost:5211")]
    [InlineData("http://localhost:5211/")]
    [InlineData("https://api.operations.example")]
    public void Validate_accepts_http_or_https_origins(string baseUrl)
    {
        var result = validator.Validate(Options.DefaultName, new ApiProxyOptions { BaseUrl = baseUrl });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost:5211")]
    [InlineData("ftp://localhost:5211")]
    [InlineData("http://localhost:5211/api")]
    [InlineData("http://localhost:5211?tenant=default")]
    [InlineData("http://localhost:5211/#portal")]
    public void Validate_rejects_invalid_or_path_based_origins(string baseUrl)
    {
        var result = validator.Validate(Options.DefaultName, new ApiProxyOptions { BaseUrl = baseUrl });

        result.Failed.ShouldBeTrue();
    }
}
