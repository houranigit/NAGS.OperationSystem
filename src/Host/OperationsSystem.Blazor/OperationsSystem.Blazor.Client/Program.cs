using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OperationsSystem.Blazor.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddPortalClientServices();

await builder.Build().RunAsync();
