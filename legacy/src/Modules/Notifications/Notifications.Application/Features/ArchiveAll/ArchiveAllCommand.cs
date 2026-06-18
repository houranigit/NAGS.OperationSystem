using BuildingBlocks.Application.Abstractions.Commands;

namespace Notifications.Application.Features.ArchiveAll;

/// <summary>
/// Soft-deletes every non-archived notification for <paramref name="UserId"/>. Powers the
/// mobile inbox "Clear all" button and any future portal "clear inbox" affordance.
/// </summary>
public sealed record ArchiveAllCommand(Guid UserId) : ICommand;
