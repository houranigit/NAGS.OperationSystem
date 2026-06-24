using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client;

/// <summary>
/// Registers the portal's client-side services. Called from both the server host (for prerender and
/// the initial interactive-server render) and the WebAssembly host so Interactive Auto behaves
/// identically in both runtimes.
/// </summary>
public static class PortalClientServices
{
    public static IServiceCollection AddPortalClientServices(this IServiceCollection services)
    {
        services.AddRadzenComponents();

        services.AddScoped<AuthTokenStore>();
        services.AddScoped<LocaleState>();
        services.AddScoped<GridPreferences>();
        services.AddScoped<ClientTokenRefresher>();
        services.AddScoped<BrowserApiClient>();
        services.AddScoped<IdentityApiClient>();
        services.AddScoped<MasterDataApiClient>();
        services.AddScoped<AuditApiClient>();
        services.AddScoped<AuthSession>();

        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, PortalAuthStateProvider>();

        return services;
    }
}
