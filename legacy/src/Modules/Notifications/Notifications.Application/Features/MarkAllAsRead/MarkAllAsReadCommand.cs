using BuildingBlocks.Application.Abstractions.Commands;

namespace Notifications.Application.Features.MarkAllAsRead;

public sealed record MarkAllAsReadCommand(Guid UserId) : ICommand;
