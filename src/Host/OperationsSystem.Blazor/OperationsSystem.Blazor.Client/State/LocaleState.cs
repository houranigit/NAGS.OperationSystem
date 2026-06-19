using Microsoft.JSInterop;

namespace OperationsSystem.Blazor.Client.State;

/// <summary>
/// Holds the active UI language and text direction. Components read <see cref="Language"/> for
/// API <c>Accept-Language</c> and subscribe to <see cref="Changed"/> to re-render on a toggle.
/// </summary>
public sealed class LocaleState(IJSRuntime jsRuntime)
{
    public const string English = "en";
    public const string Arabic = "ar";

    public string Language { get; private set; } = English;

    public string Direction => Language == Arabic ? "rtl" : "ltr";

    public bool IsRightToLeft => Language == Arabic;

    public event Action? Changed;

    public async Task SetLanguageAsync(string language)
    {
        var normalized = language == Arabic ? Arabic : English;
        if (normalized == Language)
            return;

        Language = normalized;
        await ApplyToDocumentAsync();
        Changed?.Invoke();
    }

    public Task ToggleAsync() => SetLanguageAsync(Language == English ? Arabic : English);

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
