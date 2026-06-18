using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Tool;
using Store.Domain.Aggregates.Tool;

namespace Store.Application.Features.Tool.Queries.GetToolDetails;

public sealed class GetToolDetailsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetToolDetailsQuery, ToolDetailsDto>
{
    public async Task<Result<ToolDetailsDto>> Handle(
        GetToolDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var id = ToolId.From(request.Id);

        var dto = await db.Tools
            .Where(t => t.Id == id)
            .Select(t => new ToolDetailsDto(
                t.Id.Value,
                t.Name,
                t.Description,
                t.IsActive,
                t.Equipments
                    .OrderBy(e => e.FactoryId)
                    .Select(e => new ToolEquipmentDto(e.Id.Value, e.FactoryId, e.SerialId, e.CalibrationDate))
                    .ToList(),
                t.CreatedAt,
                t.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return dto is null ? Error.NotFound("Tool was not found.") : dto;
    }
}
