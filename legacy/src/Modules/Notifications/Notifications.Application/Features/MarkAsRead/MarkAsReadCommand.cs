using BuildingBlocks.Application.Abstractions.Commands;

namespace Notifications.Application.Features.MarkAsRead;

public sealed record MarkAsReadCommand(Guid UserId, Guid NotificationId) : ICommand;
