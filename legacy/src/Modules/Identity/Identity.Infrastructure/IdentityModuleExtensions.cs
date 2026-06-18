using System.Text;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Outbox;
using Identity.Application.Abstractions;
using Identity.Application.EmailTemplates;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;
using Identity.Domain.Services;
using Identity.Infrastructure.Configuration;
using Identity.Infrastructure.EmailTemplates;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Repositories;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Quartz;

namespace Identity.Infrastructure;

public static class IdentityModuleExtensions
{
    /// <summary>
    /// Registers Identity persistence, domain services, and JWT bearer authentication (API-style default scheme).
    /// </summary>
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityModuleCore(configuration);
        services.AddIdentityJwtAuthentication(configuration);
        return services;
    }

    /// <summary>
    /// Registers Identity persistence and domain services without configuring authentication.
    /// Use with a host that composes cookie + JWT (e.g. Blazor Server) and calls authentication registration separately.
    /// </summary>
    public static IServiceCollection AddIdentityModuleCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        JwtConfiguration.EnsureValid(configuration);

        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));

        services.AddDbContext<IdentityDbContext>((sp, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<IdentityDbContext>());
        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<IdentityDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        services.AddScoped<IPasswordHistoryRepository, PasswordHistoryRepository>();

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<PasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IInvitationTokenGenerator, InvitationTokenGenerator>();

        services.Configure<InvitationEmailSettings>(configuration.GetSection("PlatformSettings"));
        services.AddSingleton<IInvitationEmailComposer, InvitationEmailComposer>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("OutboxProcessor.Identity");
            q.AddJob<OutboxProcessorJob<IdentityDbContext>>(opts => opts.WithIdentity(jobKey));
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(10).RepeatForever()));
        });

        return services;
    }

    /// <summary>
    /// Registers JWT bearer as the default authentication scheme (typical for API-only hosts).
    /// </summary>
    public static IServiceCollection AddIdentityJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        JwtConfiguration.EnsureValid(configuration);
        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
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
            });

        services.AddAuthorizationBuilder();

        return services;
    }
}
