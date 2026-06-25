using System.Globalization;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Localization;

namespace OperationsSystem.Blazor.Client.State;

/// <summary>
/// Holds the active UI language and text direction. Components read <see cref="Language"/> for
/// API <c>Accept-Language</c> and subscribe to <see cref="Changed"/> to re-render on a toggle.
/// </summary>
public sealed class LocaleState(IJSRuntime jsRuntime)
{
    public const string English = UiText.English;
    public const string Arabic = UiText.Arabic;
    public const string StorageKey = "operations-system.language";

    public string Language { get; private set; } = English;

    public string Direction => Language == Arabic ? "rtl" : "ltr";

    public bool IsRightToLeft => Language == Arabic;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var stored = await ReadStoredLanguageAsync();
        await ApplyLanguageAsync(stored ?? English, persist: false);
    }

    public async Task SetLanguageAsync(string language)
    {
        var normalized = language == Arabic ? Arabic : English;
        if (normalized == Language)
            return;

        await ApplyLanguageAsync(normalized, persist: true);
    }

    public Task ToggleAsync() => SetLanguageAsync(Language == English ? Arabic : English);

    private async Task ApplyLanguageAsync(string language, bool persist)
    {
        Language = language;
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        UiText.SetLanguage(language);

        if (persist)
            await WriteStoredLanguageAsync(language);

        await ApplyToDocumentAsync();
        Changed?.Invoke();
    }

    private async Task<string?> ReadStoredLanguageAsync()
    {
        try
        {
            return await jsRuntime.InvokeAsync<string?>("operationsSystem.storage.get", StorageKey);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task WriteStoredLanguageAsync(string language)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("operationsSystem.storage.set", StorageKey, language);
        }
        catch (JSException)
        {
            // Persistence is best-effort.
        }
    }

    private async Task ApplyToDocumentAsync()
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("operationsSystem.dom.setDirection", Direction, Language);
        }
        catch (JSException)
        {
            // Direction is a progressive enhancement; ignore if the helper is unavailable.
        }
    }
}
