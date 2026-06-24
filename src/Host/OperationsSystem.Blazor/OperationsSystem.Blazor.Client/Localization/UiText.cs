using System.Globalization;
using System.Resources;

namespace OperationsSystem.Blazor.Client.Localization;

/// <summary>
/// Resolves UI strings from embedded .resx resources for the active language.
/// English literals in <see cref="UiStrings"/> act as a compile-time fallback when a key is missing.
/// </summary>
public static class UiText
{
    public const string English = "en";
    public const string Arabic = "ar";

    private static readonly ResourceManager Manager = new(
        "OperationsSystem.Blazor.Client.Localization.Resources.UiStrings",
        typeof(UiText).Assembly);

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo(English);
    private static readonly CultureInfo ArabicCulture = CultureInfo.GetCultureInfo(Arabic);

    public static string CurrentLanguage { get; private set; } = English;

    public static bool IsArabic => CurrentLanguage == Arabic;

    public static void SetLanguage(string language)
    {
        CurrentLanguage = language == Arabic ? Arabic : English;
    }

    public static string Get(string key, string englishFallback)
    {
        var culture = CurrentLanguage == Arabic ? ArabicCulture : EnglishCulture;
        var localized = Manager.GetString(key, culture);
        if (!string.IsNullOrEmpty(localized))
            return localized;

        if (CurrentLanguage != English)
        {
            var english = Manager.GetString(key, EnglishCulture);
            if (!string.IsNullOrEmpty(english))
                return english;
        }

        return englishFallback;
    }
}
