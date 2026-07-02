using FluentValidation;

namespace Identity.Application.Security;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static IRuleBuilderOptions<T, string> UseIdentityPasswordPolicy<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty()
            .MinimumLength(MinLength)
            .MaximumLength(MaxLength)
            .Must(ContainsUppercase).WithMessage("{PropertyName} must contain at least one uppercase letter.")
            .Must(ContainsLowercase).WithMessage("{PropertyName} must contain at least one lowercase letter.")
            .Must(ContainsDigit).WithMessage("{PropertyName} must contain at least one digit.")
            .Must(ContainsSymbol).WithMessage("{PropertyName} must contain at least one symbol.");

    public static IReadOnlyList<string> Validate(string? password, string settingName)
    {
        var failures = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            failures.Add($"{settingName} is required.");
            return failures;
        }

        if (password.Length < MinLength)
            failures.Add($"{settingName} must be at least {MinLength} characters.");

        if (password.Length > MaxLength)
            failures.Add($"{settingName} must be at most {MaxLength} characters.");

        if (!ContainsUppercase(password))
            failures.Add($"{settingName} must contain at least one uppercase letter.");

        if (!ContainsLowercase(password))
            failures.Add($"{settingName} must contain at least one lowercase letter.");

        if (!ContainsDigit(password))
            failures.Add($"{settingName} must contain at least one digit.");

        if (!ContainsSymbol(password))
            failures.Add($"{settingName} must contain at least one symbol.");

        return failures;
    }

    private static bool ContainsUppercase(string password) => password.Any(char.IsUpper);

    private static bool ContainsLowercase(string password) => password.Any(char.IsLower);

    private static bool ContainsDigit(string password) => password.Any(char.IsDigit);

    private static bool ContainsSymbol(string password) => password.Any(c => !char.IsLetterOrDigit(c));
}
