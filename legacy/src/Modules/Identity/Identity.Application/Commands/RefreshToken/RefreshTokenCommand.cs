using BuildingBlocks.Application.Abstractions.Commands;
using Identity.Application.Commands.Login;

namespace Identity.Application.Commands.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<LoginResult>;
