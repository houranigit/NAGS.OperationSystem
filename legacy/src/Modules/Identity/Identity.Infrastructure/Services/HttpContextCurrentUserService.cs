using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Identity.Infrastructure.Services;

public sealed class HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserName => Principal?.FindFirstValue("username");

    public string? UserType => Principal?.FindFirstValue("userType");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool HasPermission(string permission) =>
        Principal?.Claims
            .Where(c => c.Type == "permissions")
            .Any(c => c.Value == permission) ?? false;
}
