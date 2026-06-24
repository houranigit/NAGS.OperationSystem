using System.Text.Json;
using Audit.Application.Abstractions;
using Audit.Application.Contracts;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Contracts.Auditing;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;

namespace Audit.Application.Features.Trails;

// --- Paged list -----------------------------------------------------------

public sealed record GetAuditTrailsQuery(
    int Page = 1,
    int PageSize = 20,
    string? SubjectType = null,
    Guid? SubjectId = null,
    string? EntityType = null,
    Guid? EntityId = null,
    Guid? ActorId = null,
    string? Action = null,
    string? Sort = null) : IQuery<PagedResult<AuditTrailListItemDto>>;

public sealed class GetAuditTrailsQueryHandler(IAuditDbContext db)
    : IQueryHandler<GetAuditTrailsQuery, PagedResult<AuditTrailListItemDto>>
{
    public async Task<Result<PagedResult<AuditTrailListItemDto>>> Handle(GetAuditTrailsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.AuditTrails.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.SubjectType))
            query = query.Where(a => a.RootSubjectType == request.SubjectType);

        if (request.SubjectId is { } subjectId)
            query = query.Where(a => a.RootSubjectId == subjectId);

        if (!string.IsNullOrWhiteSpace(request.EntityType))
            query = query.Where(a => a.EntityType == request.EntityType);

        if (request.EntityId is { } entityId)
            query = query.Where(a => a.EntityId == entityId);

        if (request.ActorId is { } actorId)
            query = query.Where(a => a.ActorId == actorId);

        if (!string.IsNullOrWhiteSpace(request.Action))
            query = query.Where(a => a.Action == request.Action);

        var total = await query.LongCountAsync(cancellationToken);

        // Newest-first by default; the trail is a timeline.
        var ascending = SortSpec.Parse(request.Sort) is { Field: "occurredat", Descending: false };
        query = ascending
            ? query.OrderBy(a => a.OccurredOnUtc).ThenBy(a => a.Id)
            : query.OrderByDescending(a => a.OccurredOnUtc).ThenByDescending(a => a.Id);

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditTrailListItemDto(
                a.Id, a.OccurredOnUtc, a.ActorId, a.ActorDisplayName, a.IsSystemActor,
                a.Module, a.RootSubjectType, a.RootSubjectId, a.EntityType, a.EntityId,
                a.Action, a.CorrelationId))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditTrailListItemDto>(rows, page, pageSize, total);
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetAuditTrailByIdQuery(Guid Id) : IQuery<AuditTrailDto>;

public sealed class GetAuditTrailByIdQueryHandler(IAuditDbContext db)
    : IQueryHandler<GetAuditTrailByIdQuery, AuditTrailDto>
{
    public async Task<Result<AuditTrailDto>> Handle(GetAuditTrailByIdQuery request, CancellationToken cancellationToken)
    {
        var a = await db.AuditTrails.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (a is null)
            return Error.NotFound("Audit trail not found.", "Audit.Trail.NotFound");

        var changes = string.IsNullOrEmpty(a.ChangesJson)
            ? []
            : JsonSerializer.Deserialize<List<AuditFieldChange>>(a.ChangesJson) ?? [];

        var changeDtos = changes
            .Select(c => new AuditFieldChangeDto(c.Field, c.Before, c.After))
            .ToList();

        return new AuditTrailDto(
            a.Id, a.OccurredOnUtc, a.ActorId, a.ActorDisplayName, a.IsSystemActor,
            a.Module, a.RootSubjectType, a.RootSubjectId, a.EntityType, a.EntityId,
            a.Action, a.CorrelationId, changeDtos, a.Metadata);
    }
}
