using BuildingBlocks.Application.Abstractions.Commands;

namespace Identity.Application.Commands.Login;

public sealed record LoginCommand(
    string EmailOrUsername,
    string Password,
    string? DeviceInfo,
    string? IpAddress,
    string? UserAgent
) : ICommand<LoginResult>;

public sealed record LoginResult(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    Guid UserId,
    string Username,
    string Email,
    string UserType,
    IReadOnlyList<string> Permissions
);
