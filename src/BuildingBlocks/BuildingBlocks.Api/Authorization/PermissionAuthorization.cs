using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Api.Authorization;

/// <summary>Shared claim/policy constants for permission-based authorization.</summary>
public static class PermissionPolicy
{
    /// <summary>Claim type carrying a single granted permission.</summary>
    public const string ClaimType = "permission";

    /// <summary>Prefix that marks a dynamically-generated permission policy.</summary>
    public const string Prefix = "perm:";
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class AnyPermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}

/// <summary>
/// Grants access only when the caller both holds the permission claim AND has a
/// <see cref="UserType"/> that the permission is compatible with (per the composed
/// <see cref="IPermissionRegistry"/>). This is the server-side guarantee that a forged or
/// mistakenly-stored permission cannot grant access to an incompatible account type: a Station
/// Staff, Customer Contact, or Viewer Only token carrying an incompatible mutation permission is
/// rejected.
/// </summary>
public sealed class PermissionAuthorizationHandler(IPermissionRegistry permissionRegistry)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var hasPermission = context.User.Claims.Any(c =>
            c.Type == PermissionPolicy.ClaimType && c.Value == requirement.Permission);

        if (!hasPermission)
            return Task.CompletedTask;

        // The permission must be known and compatible with the caller's user type. Unknown
        // permissions and incompatible user types both fail closed.
        if (!Enum.TryParse<UserType>(context.User.FindFirst(AuthorizationClaimTypes.UserType)?.Value, out var userType))
            return Task.CompletedTask;

        if (permissionRegistry.IsCompatibleWith(requirement.Permission, userType))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

public sealed class AnyPermissionAuthorizationHandler(IPermissionRegistry permissionRegistry)
    : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AnyPermissionRequirement requirement)
    {
        if (!Enum.TryParse<UserType>(context.User.FindFirst(AuthorizationClaimTypes.UserType)?.Value, out var userType))
            return Task.CompletedTask;

        foreach (var permission in requirement.Permissions)
        {
            var hasPermission = context.User.Claims.Any(c =>
                c.Type == PermissionPolicy.ClaimType && c.Value == permission);

            if (hasPermission && permissionRegistry.IsCompatibleWith(permission, userType))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Generates a permission policy on demand for any policy name prefixed with
/// <see cref="PermissionPolicy.Prefix"/>, so endpoints can require arbitrary permissions
/// without pre-registering each policy.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[PermissionPolicy.Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}

public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, AnyPermissionAuthorizationHandler>();
        return services;
    }

    /// <summary>
    /// Registers the composed <see cref="IPermissionRegistry"/> built from every module's
    /// <see cref="BuildingBlocks.Contracts.Authorization.IPermissionCatalog"/> registered in DI.
    /// </summary>
    public static IServiceCollection AddPermissionRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<IPermissionRegistry, PermissionRegistry>();
        return services;
    }

    /// <summary>Requires the caller to hold <paramref name="permission"/> (a permission claim).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization($"{PermissionPolicy.Prefix}{permission}");
        return builder;
    }

    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new AnyPermissionRequirement(permissions))
            .Build());
        return builder;
    }
}
