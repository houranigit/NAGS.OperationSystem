using Audit.Application.Features.Trails;
using Audit.Domain.Authorization;
using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Audit.Api.Endpoints;

internal static class AuditTrailEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var trails = group.MapGroup("/trails").WithTags("Audit.Trails");

        // Administrator-only, paginated, filtered by subject/entity/actor/action. Read-only:
        // there are deliberately no write or delete endpoints.
        trails.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20,
            string? subjectType = null, Guid? subjectId = null,
            string? entityType = null, Guid? entityId = null,
            Guid? actorId = null, string? action = null, string? sort = null) =>
        {
            var result = await sender.Send(
                new GetAuditTrailsQuery(page, pageSize, subjectType, subjectId, entityType, entityId, actorId, action, sort),
                ct);
            return result.ToOk();
        }).RequirePermission(AuditPermissions.Trails.View);

        trails.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAuditTrailByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(AuditPermissions.Trails.View);
    }
}
