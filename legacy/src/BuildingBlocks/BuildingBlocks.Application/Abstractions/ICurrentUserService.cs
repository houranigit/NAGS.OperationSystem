namespace BuildingBlocks.Application.Abstractions;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
    string? UserType { get; }
}
