using BuildingBlocks.Api.Results;
using BuildingBlocks.Domain.Results;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Users;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var auth = group.MapGroup("/auth").WithTags("Identity.Auth");

        auth.MapPost("/login", async (LoginRequest request, ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new LoginCommand(request.Email, request.Password, AuthCookies.ClientIp(http), AuthCookies.UserAgent(http)), ct);

            if (result.IsFailure)
                return ApiResults.Problem(result.Error);

            AuthCookies.SetRefreshToken(http, result.Value);
            return Results.Ok(new AccessTokenResponse(result.Value.AccessToken, result.Value.AccessTokenExpiresAtUtc));
        }).AllowAnonymous();

        auth.MapPost("/refresh", async (ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var token = http.Request.Cookies[AuthCookies.RefreshTokenCookie];
            if (string.IsNullOrEmpty(token))
                return ApiResults.Problem(Error.Unauthorized("No refresh token.", "Identity.Auth.NoRefreshToken"));

            var result = await sender.Send(
                new RefreshTokenCommand(token, AuthCookies.ClientIp(http), AuthCookies.UserAgent(http)), ct);

            if (result.IsFailure)
            {
                AuthCookies.ClearRefreshToken(http);
                return ApiResults.Problem(result.Error);
            }

            AuthCookies.SetRefreshToken(http, result.Value);
            return Results.Ok(new AccessTokenResponse(result.Value.AccessToken, result.Value.AccessTokenExpiresAtUtc));
        }).AllowAnonymous();

        auth.MapPost("/logout", async (ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var token = http.Request.Cookies[AuthCookies.RefreshTokenCookie];
            var result = await sender.Send(new LogoutCommand(token), ct);
            AuthCookies.ClearRefreshToken(http);
            return result.ToNoContent();
        }).RequireAuthorization();

        auth.MapPost("/activate", async (ActivateAccountRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ActivateAccountCommand(request.Email, request.InvitationToken, request.NewPassword), ct);
            return result.ToNoContent();
        }).AllowAnonymous();

        auth.MapPost("/change-password", async (ChangePasswordRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), ct);
            return result.ToNoContent();
        }).RequireAuthorization();

        auth.MapPost("/confirm-email-change", async (ConfirmEmailChangeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ConfirmEmailChangeCommand(request.Token, request.NewEmail), ct);
            return result.ToNoContent();
        }).AllowAnonymous();

        group.MapGet("/me", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCurrentUserQuery(), ct);
            return result.ToOk();
        }).RequireAuthorization().WithTags("Identity.Auth");
    }
}
