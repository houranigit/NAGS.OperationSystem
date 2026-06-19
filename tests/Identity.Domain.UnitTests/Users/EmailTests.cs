using Identity.Domain.Users;
using Shouldly;

namespace Identity.Domain.UnitTests.Users;

public class EmailTests
{
    [Theory]
    [InlineData("USER@NAGS.SA", "user@nags.sa")]
    [InlineData("  Admin@Example.Com ", "admin@example.com")]
    public void Create_normalizes_to_lowercase_trimmed(string input, string expected)
    {
        var result = Email.Create(input);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing@domain")]
    public void Create_rejects_invalid(string input)
    {
        var result = Email.Create(input);

        result.IsFailure.ShouldBeTrue();
    }
}
