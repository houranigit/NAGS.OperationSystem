namespace BuildingBlocks.Application.Abstractions;

public interface IRequirePermission
{
    string RequiredPermission { get; }
}
