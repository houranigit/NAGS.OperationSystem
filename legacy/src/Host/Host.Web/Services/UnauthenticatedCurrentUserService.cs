using BuildingBlocks.Application.Abstractions;

namespace Host.Web.Services;

public sealed class UnauthenticatedCurrentUserService : ICurrentUserService
{
    public Guid? UserId => null;

    public string? UserName => null;

    public bool IsAuthenticated => false;

    public bool HasPermission(string permission) => false;

    public string? UserType => null;
}
