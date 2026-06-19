using Identity.Application.Contracts;
using Microsoft.AspNetCore.Http;

namespace Identity.Api.Endpoints;

internal static class AuthCookies
{
    public const string RefreshTokenCookie = "refreshToken";

    public static void SetRefreshToken(HttpContext http, AuthTokensDto tokens)
    {
        http.Response.Cookies.Append(RefreshTokenCookie, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = tokens.RefreshTokenExpiresAtUtc
        });
    }

    public static void ClearRefreshToken(HttpContext http) =>
        http.Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });

    public static string? ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    public static string? UserAgent(HttpContext http) => http.Request.Headers.UserAgent.ToString();
}
