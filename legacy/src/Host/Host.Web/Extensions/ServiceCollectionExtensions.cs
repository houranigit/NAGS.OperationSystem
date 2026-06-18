using Host.Web.Authorization;
using Identity.Infrastructure.Configuration;
using Identity.Infrastructure.Services;
using Identity.Domain.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Host.Web.Services;
using Radzen;
using System.Text;

namespace Host.Web.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Chooses JWT when the client sends a Bearer token (or <c>access_token</c> on hub negotiation);
    /// otherwise cookie auth for the Blazor UI. Avoids registering both schemes on the default policy,
    /// which produces a broken challenge (404 + mixed Location / WWW-Authenticate) for browser requests to <c>/</c>.
    /// </summary>
    public const string DynamicAuthenticationScheme = "Dynamic";

    public static IServiceCollection AddWebServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddRadzenComponents();
        services.AddSingleton<IScopedMediator, ScopedMediator>();
        services.AddScoped<IGridSettingsStorage, GridSettingsStorage>();
        services.AddScoped<ILookupCache, LookupCache>();
        services.AddScoped<ToastService>();
        services.AddScoped<PageTitleService>();
        return services;
    }

    /// <summary>
    /// Registers cookie auth plus JWT bearer, with a default <see cref="DynamicAuthenticationScheme"/> that
    /// picks JWT when a Bearer token (or hub <c>access_token</c>) is present and cookie auth otherwise.
    /// Identity and Blazor use the default policy; <c>/api/mobile</c> uses the <c>MobileJwt</c> policy (bearer only).
    /// </summary>
    public static IServiceCollection AddOperationsWebAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        JwtConfiguration.EnsureValid(configuration);
        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = DynamicAuthenticationScheme;
                options.DefaultAuthenticateScheme = DynamicAuthenticationScheme;
                options.DefaultChallengeScheme = DynamicAuthenticationScheme;
            })
            .AddPolicyScheme(DynamicAuthenticationScheme, "Cookie or JWT per request", policy =>
            {
                policy.ForwardDefaultSelector = ctx =>
                {
                    var path = ctx.Request.Path;
                    if (path.StartsWithSegments("/hubs"))
                    {
                        var accessToken = ctx.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(accessToken))
                            return JwtBearerDefaults.AuthenticationScheme;
                    }

                    var authHeader = ctx.Request.Headers.Authorization.ToString();
                    if (!string.IsNullOrEmpty(authHeader)
                        && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return JwtBearerDefaults.AuthenticationScheme;

                    return CookieAuthenticationDefaults.AuthenticationScheme;
                };
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/login";
                options.AccessDeniedPath = "/access-denied";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                    ClockSkew = TimeSpan.Zero
                };

                // SignalR clients pass the JWT in the query string during the WebSocket
                // negotiation, since browsers cannot set custom Authorization headers on
                // the WS upgrade. Lift it from there into the standard pipeline.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(PortalPolicies.ManagePortal, policy =>
                policy.RequireClaim("permissions", Permissions.Portal.Manage));

            options.AddPolicy(PortalPolicies.ViewScheduler, policy =>
                policy.RequireClaim("permissions", Permissions.Scheduler.Read));

            options.AddPolicy(PortalPolicies.CreateFlights, policy =>
                policy.RequireClaim("permissions", Permissions.Flights.Create));

            options.AddPolicy(PortalPolicies.UpdateFlights, policy =>
                policy.RequireClaim("permissions", Permissions.Flights.Update));

            options.AddPolicy("MobileJwt", policy =>
            {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });
        });

        services.AddCascadingAuthenticationState();

        return services;
    }
}
