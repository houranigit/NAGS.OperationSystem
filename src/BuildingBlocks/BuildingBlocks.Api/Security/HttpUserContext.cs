using System.Security.Claims;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Api.Security;

/// <summary>Resolves the caller's business identity from the current request's JWT claims.</summary>
public sealed class HttpUserContext(IHttpContextAccessor accessor) : IUserContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            // "sub" is the JWT subject claim (Identity issues tokens with MapInboundClaims disabled).
            var value = Principal?.FindFirstValue("sub")
                ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public UserType? UserType =>
        Enum.TryParse<UserType>(Principal?.FindFirstValue(AuthorizationClaimTypes.UserType), out var type)
            ? type
            : null;

    public Guid? ExternalReferenceId =>
        Guid.TryParse(Principal?.FindFirstValue(AuthorizationClaimTypes.ExternalReference), out var id)
            ? id
            : null;

    public bool HasPermission(string permission) =>
        Principal?.Claims.Any(c => c.Type == "permission" && c.Value == permission) ?? false;
}

public static class UserContextExtensions
{
    /// <summary>Registers the claims-backed <see cref="IUserContext"/> for the request pipeline.</summary>
    public static IServiceCollection AddHttpUserContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, HttpUserContext>();
        return services;
    }
}
