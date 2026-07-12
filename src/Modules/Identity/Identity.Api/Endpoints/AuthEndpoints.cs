using BuildingBlocks.Api.RateLimiting;
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

            // MFA-enrolled accounts must complete the second step; no tokens or cookie are issued yet.
            if (result.Value.MfaRequired)
                return Results.Ok(new LoginChallengeResponse(true, result.Value.MfaToken!));

            var tokens = result.Value.Tokens!;
            AuthCookies.SetRefreshToken(http, tokens);
            return Results.Ok(new AccessTokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc));
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        auth.MapPost("/login/mfa", async (LoginMfaRequest request, ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new LoginMfaCommand(request.MfaToken, request.Code, AuthCookies.ClientIp(http), AuthCookies.UserAgent(http)), ct);

            if (result.IsFailure)
                return ApiResults.Problem(result.Error);

            AuthCookies.SetRefreshToken(http, result.Value);
            return Results.Ok(new AccessTokenResponse(result.Value.AccessToken, result.Value.AccessTokenExpiresAtUtc));
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        auth.MapPost("/mfa/enroll", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new EnrollMfaCommand(), ct);
            return result.ToOk();
        }).RequireAuthorization();

        auth.MapPost("/mfa/confirm", async (ConfirmMfaRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ConfirmMfaCommand(request.Code), ct);
            return result.ToOk();
        }).RequireAuthorization();

        auth.MapPost("/mfa/disable", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DisableMfaCommand(), ct);
            return result.ToNoContent();
        }).RequireAuthorization();

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
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        auth.MapPost("/logout", async (ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var token = http.Request.Cookies[AuthCookies.RefreshTokenCookie];
            var result = await sender.Send(new LogoutCommand(token), ct);
            AuthCookies.ClearRefreshToken(http);
            return result.ToNoContent();
        }).RequireAuthorization();

        // Mobile (bearer-only) variants: same handlers and session rotation as the web flow, but the
        // refresh token is exchanged through the JSON body instead of the httpOnly cookie.
        var mobile = auth.MapGroup("/mobile");

        mobile.MapPost("/login", async (LoginRequest request, ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new LoginCommand(request.Email, request.Password, AuthCookies.ClientIp(http), AuthCookies.UserAgent(http)), ct);

            if (result.IsFailure)
                return ApiResults.Problem(result.Error);

            if (result.Value.MfaRequired)
                return Results.Ok(new LoginChallengeResponse(true, result.Value.MfaToken!));

            return Results.Ok(ToMobileTokens(result.Value.Tokens!));
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        mobile.MapPost("/login/mfa", async (LoginMfaRequest request, ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new LoginMfaCommand(request.MfaToken, request.Code, AuthCookies.ClientIp(http), AuthCookies.UserAgent(http)), ct);

            return result.IsFailure
                ? ApiResults.Problem(result.Error)
                : Results.Ok(ToMobileTokens(result.Value));
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        mobile.MapPost("/refresh", async (MobileRefreshRequest request, ISender sender, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return ApiResults.Problem(Error.Unauthorized("No refresh token.", "Identity.Auth.NoRefreshToken"));

            var result = await sender.Send(
                new RefreshTokenCommand(request.RefreshToken, AuthCookies.ClientIp(http), AuthCookies.UserAgent(http)), ct);

            return result.IsFailure
                ? ApiResults.Problem(result.Error)
                : Results.Ok(ToMobileTokens(result.Value));
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        mobile.MapPost("/logout", async (MobileLogoutRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new LogoutCommand(request.RefreshToken), ct);
            return result.ToNoContent();
        }).RequireAuthorization();

        auth.MapPost("/activate", async (ActivateAccountRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ActivateAccountCommand(request.Email, request.InvitationToken, request.NewPassword), ct);
            return result.ToNoContent();
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        auth.MapPost("/change-password", async (ChangePasswordRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), ct);
            return result.ToNoContent();
        }).RequireAuthorization();

        auth.MapPost("/confirm-email-change", async (ConfirmEmailChangeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ConfirmEmailChangeCommand(request.Token, request.NewEmail), ct);
            return result.ToNoContent();
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, ISender sender, CancellationToken ct) =>
        {
            // Always 204 regardless of whether the email exists (non-enumerating).
            await sender.Send(new ForgotPasswordCommand(request.Email), ct);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ResetPasswordCommand(request.Token, request.NewPassword), ct);
            return result.ToNoContent();
        }).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AnonymousAuth);

        group.MapGet("/me", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCurrentUserQuery(), ct);
            return result.ToOk();
        }).RequireAuthorization().WithTags("Identity.Auth");
    }

    private static MobileTokensResponse ToMobileTokens(Identity.Application.Contracts.AuthTokensDto tokens) =>
        new(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc);
}
