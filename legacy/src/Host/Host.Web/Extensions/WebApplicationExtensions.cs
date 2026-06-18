using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.Application.Commands.ActivateAccount;
using Identity.Application.Commands.Login;
using Identity.Application.Commands.Logout;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Host.Web.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Http-only cookie used to revoke the server session on logout (same refresh token as the Identity API).
    /// </summary>
    public const string RefreshTokenCookieName = "__OperationsManager.RefreshToken";

    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/cookie-login", async (
            HttpContext httpContext,
            ISender sender) =>
        {
            var form = await httpContext.Request.ReadFormAsync();
            var email = form["email"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();
            var rememberMe = form["rememberMe"].ToString() == "true";

            if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/'))
                returnUrl = "/";

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var result = await sender.Send(
                new LoginCommand(email, password, null, ipAddress, userAgent),
                httpContext.RequestAborted);

            if (result.IsFailure)
            {
                var errorEncoded = Uri.EscapeDataString(result.Error.Description);
                return Results.Redirect(
                    $"/login?error={errorEncoded}&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            var auth = result.Value;
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, auth.UserId.ToString()),
                new(JwtRegisteredClaimNames.Email, auth.Email),
                new("username", auth.Username),
                new("userType", auth.UserType),
            };

            foreach (var permission in auth.Permissions)
                claims.Add(new Claim("permissions", permission));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authProps = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : DateTimeOffset.UtcNow.AddHours(8)
            };

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProps);

            httpContext.Response.Cookies.Append(
                RefreshTokenCookieName,
                auth.RefreshToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Expires = new DateTimeOffset(auth.RefreshTokenExpiresAt)
                });

            return Results.Redirect(returnUrl);
        }).AllowAnonymous()
          .DisableAntiforgery();

        app.MapPost("/api/auth/cookie-activate", async (
            HttpContext httpContext,
            ISender sender) =>
        {
            var form = await httpContext.Request.ReadFormAsync();
            var email = form["email"].ToString();
            var invitationToken = form["invitationToken"].ToString();
            var password = form["password"].ToString();
            var confirmPassword = form["confirmPassword"].ToString();
            var returnUrl = form["returnUrl"].ToString();
            var rememberMe = form["rememberMe"].ToString() == "true";

            if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/'))
                returnUrl = "/";

            IResult RedirectActivateError(string message)
            {
                var errorEncoded = Uri.EscapeDataString(message);
                return Results.Redirect(
                    $"/activate?error={errorEncoded}&email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(invitationToken)}&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
                return RedirectActivateError("Passwords do not match.");

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var result = await sender.Send(
                new ActivateAccountCommand(email, invitationToken, password, null, ipAddress, userAgent),
                httpContext.RequestAborted);

            if (result.IsFailure)
                return RedirectActivateError(result.Error.Description);

            var auth = result.Value;
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, auth.UserId.ToString()),
                new(JwtRegisteredClaimNames.Email, auth.Email),
                new("username", auth.Username),
                new("userType", auth.UserType),
            };

            foreach (var permission in auth.Permissions)
                claims.Add(new Claim("permissions", permission));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authProps = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : DateTimeOffset.UtcNow.AddHours(8)
            };

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProps);

            httpContext.Response.Cookies.Append(
                RefreshTokenCookieName,
                auth.RefreshToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Expires = new DateTimeOffset(auth.RefreshTokenExpiresAt)
                });

            return Results.Redirect(returnUrl);
        }).AllowAnonymous()
          .DisableAntiforgery();

        app.MapGet("/api/auth/cookie-logout", async (
            HttpContext httpContext,
            ISender sender) =>
        {
            var refreshToken = httpContext.Request.Cookies[RefreshTokenCookieName];
            if (!string.IsNullOrEmpty(refreshToken))
                await sender.Send(new LogoutCommand(refreshToken), httpContext.RequestAborted);

            httpContext.Response.Cookies.Delete(RefreshTokenCookieName);
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Results.Redirect("/login");
        }).AllowAnonymous();

        return app;
    }
}
