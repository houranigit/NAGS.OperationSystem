using System.Globalization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddPortalClientServices();

var host = builder.Build();
await ApplyStoredLanguageAsync(host.Services);
await host.RunAsync();

static async Task ApplyStoredLanguageAsync(IServiceProvider services)
{
    var language = UiText.English;

    try
    {
        var js = services.GetRequiredService<IJSRuntime>();
        var stored = await js.InvokeAsync<string?>("operationsSystem.storage.get", LocaleState.StorageKey);
        if (stored == UiText.Arabic)
            language = UiText.Arabic;
    }
    catch (JSException)
    {
        // Startup localization is best-effort; LocaleState will apply the default language.
    }

    var culture = CultureInfo.GetCultureInfo(language);
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;
    UiText.SetLanguage(language);
}
