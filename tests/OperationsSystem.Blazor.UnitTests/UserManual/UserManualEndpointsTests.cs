using OperationsSystem.Blazor.UserManual;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.UserManual;

public sealed class UserManualEndpointsTests
{
    [Fact]
    public void EmbeddedManual_IsSelfContainedAndUsesCapturedApplicationScreens()
    {
        using var stream = UserManualEndpoints.OpenResource();
        using var reader = new StreamReader(stream);

        var html = reader.ReadToEnd();

        html.ShouldContain("Flight scheduling baseline");
        html.ShouldContain("Mobile is not supported by this backend baseline");
        html.ShouldContain("NAGS Operations Field Guide");
        html.ShouldContain("Captured from the running applications");
        html.ShouldContain("data:image/png;base64,");
        html.ShouldNotContain("src=\"/screenshots/");
        html.ShouldNotContain("href=\"/downloads/");
    }
}
