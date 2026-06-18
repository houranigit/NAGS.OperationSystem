using BuildingBlocks.Application.Abstractions.Commands;

namespace Identity.Application.Commands.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand;
