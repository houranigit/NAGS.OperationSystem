using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Identity.IntegrationTests;

public sealed class OperationsDataProtectionOptionsValidatorTests
{
    [Fact]
    public void Production_requires_a_persisted_key_ring_path()
    {
        var validator = new OperationsDataProtectionOptionsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(null, new OperationsDataProtectionOptions());

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DataProtection:KeyRingPath is required in Production");
    }

    [Fact]
    public void Production_accepts_a_persisted_key_ring_path()
    {
        var validator = new OperationsDataProtectionOptionsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(null, new OperationsDataProtectionOptions
        {
            KeyRingPath = "/var/lib/operations-system/data-protection"
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Development_allows_default_key_storage()
    {
        var validator = new OperationsDataProtectionOptionsValidator(new TestHostEnvironment(Environments.Development));

        var result = validator.Validate(null, new OperationsDataProtectionOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Application_name_is_required()
    {
        var validator = new OperationsDataProtectionOptionsValidator(new TestHostEnvironment(Environments.Development));

        var result = validator.Validate(null, new OperationsDataProtectionOptions
        {
            ApplicationName = " "
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DataProtection:ApplicationName is required.");
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Identity.IntegrationTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
