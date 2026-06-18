namespace BuildingBlocks.Application.Abstractions;

public interface IOutboxWriter
{
    void Write(string eventType, string content);
}
