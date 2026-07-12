using Microsoft.Extensions.Options;
using OperationsSystem.Blazor.Api;
using OperationsSystem.Blazor.Client;
using OperationsSystem.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddApiProxyHttpClient();
builder.Services.AddSingleton<IValidateOptions<ApiProxyOptions>, ApiProxyOptionsValidator>();
builder.Services.AddOptions<ApiProxyOptions>()
    .Bind(builder.Configuration.GetSection(ApiProxyOptions.SectionName))
    .ValidateOnStart();

// Shared portal client services (Radzen, API client, auth/locale state) so prerender and the
// interactive-server render use the same services as the WebAssembly runtime.
builder.Services.AddPortalClientServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");

// Opt-in HTTPS redirect so HTTP-only Windows/IIS hosting (e.g. http://operation.nags-ksa.com) works.
if (app.Configuration.GetValue("ForceHttpsRedirect", false))
    app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapApiProxy();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(OperationsSystem.Blazor.Client._Imports).Assembly);

app.Run();
