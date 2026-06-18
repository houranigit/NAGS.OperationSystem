namespace Store.Domain.Aggregates.Tool;

public interface IToolRepository
{
    Task<Tool?> GetByIdAsync(ToolId id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, ToolId? excludeId = null, CancellationToken ct = default);
    void Add(Tool tool);
    void Update(Tool tool);
}
