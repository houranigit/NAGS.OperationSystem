using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Identity.Infrastructure.Security;

/// <summary>Resolves the authenticated principal from the current HTTP request's claims.</summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            var value = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
