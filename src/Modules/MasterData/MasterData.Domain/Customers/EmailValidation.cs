using System.Text.RegularExpressions;

namespace MasterData.Domain.Customers;

/// <summary>Lightweight email format check used by MasterData contact/official-email fields.</summary>
internal static partial class EmailValidation
{
    public static bool IsValid(string normalized) => EmailRegex().IsMatch(normalized);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
