// This file is kept for backward compatibility.
// The canonical ICurrentUserService is now in BuildingBlocks.Application.Abstractions.
// HTTP implementations in Infrastructure should implement BuildingBlocks.Application.Abstractions.ICurrentUserService.
namespace BuildingBlocks.Infrastructure.Services;

[Obsolete("Use BuildingBlocks.Application.Abstractions.ICurrentUserService instead.")]
public interface ICurrentUserService : BuildingBlocks.Application.Abstractions.ICurrentUserService
{
}
