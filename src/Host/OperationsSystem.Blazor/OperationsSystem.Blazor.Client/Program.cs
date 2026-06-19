using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped<AuthTokenStore>();
builder.Services.AddScoped<BrowserApiClient>();
builder.Services.AddScoped<AuthSession>();

await builder.Build().RunAsync();
