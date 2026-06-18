using BuildingBlocks.Application.Abstractions.Commands;

namespace Identity.Application.Commands.ActivateAccount;

public sealed record ActivateAccountCommand(
    string Email,
    string InvitationToken,
    string Password,
    string? DeviceInfo = null,
    string? IpAddress = null,
    string? UserAgent = null
) : ICommand<ActivateAccountResult>;

public sealed record ActivateAccountResult(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    Guid UserId,
    string Username,
    string Email,
    string UserType,
    IReadOnlyList<string> Permissions);
