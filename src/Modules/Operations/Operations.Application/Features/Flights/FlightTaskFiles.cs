using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record GetMobileFlightTaskAttachmentQuery(Guid FlightId, Guid RecordId, Guid AttachmentId, DateTimeOffset ExpectedUploadedAt)
    : IQuery<FlightTaskAttachmentContent>;
public sealed record FlightTaskAttachmentContent(Stream Content, string ContentType, string FileName);

public sealed class GetMobileFlightTaskAttachmentQueryHandler(IOperationsDbContext db, IMobileFlightScope scope, IFileStorage storage)
    : IQueryHandler<GetMobileFlightTaskAttachmentQuery, FlightTaskAttachmentContent>
{
    public async Task<Result<FlightTaskAttachmentContent>> Handle(GetMobileFlightTaskAttachmentQuery request, CancellationToken cancellationToken)
    {
        var actor = await scope.ResolveAsync(cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var flight = await MobileFlightQuery.ApplyReadScope(db.Flights.AsNoTracking(), actor.Value)
            .Include(f => f.TaskRecords).ThenInclude(s => s.Attachments)
            .SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        var attachment = flight?.Tasks.SingleOrDefault(s => s.Id == request.RecordId)?.Attachments.SingleOrDefault(a => a.Id == request.AttachmentId);
        if (attachment is null) return NotFound();
        if (attachment.UploadedAtUtc != request.ExpectedUploadedAt)
            return Error.PreconditionFailed("The attachment changed. Reload the flight and retry.", "Operations.FlightTask.AttachmentChanged");
        var content = await storage.OpenAsync(attachment.Details.StorageKey, cancellationToken);
        return content is null ? NotFound() : new FlightTaskAttachmentContent(content, attachment.Details.ContentType, attachment.Details.FileName);
    }
    private static Error NotFound() => Error.NotFound("Task attachment not found.", "Operations.FlightTask.AttachmentNotFound");
}

internal static class FlightTaskFiles
{
    internal static async Task<bool> MatchesAsync(IFileStorage storage, FlightTask task,
        IReadOnlyList<SaveFlightServiceAttachmentRequest>? desired, CancellationToken cancellationToken)
    {
        if (desired is null) return true;
        if (desired.Count != task.Attachments.Count) return false;
        foreach (var input in desired)
        {
            var existing = task.Attachments.SingleOrDefault(a => a.Id == input.Id);
            if (existing is null || existing.Details.Kind != (AttachmentKind)input.Kind ||
                existing.Details.FileName != NormalizeName(input.FileName) || !existing.Details.ContentType.Equals(input.ContentType.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            if (input.Content is not null && !await ContentMatchesAsync(storage, existing, input.Content, cancellationToken)) return false;
        }
        return true;
    }

    internal static async Task<bool> ContentMatchesAsync(IFileStorage storage, FlightTaskAttachment existing, byte[] bytes, CancellationToken cancellationToken)
    {
        if (existing.Details.SizeBytes != bytes.LongLength) return false;
        await using var content = await storage.OpenAsync(existing.Details.StorageKey, cancellationToken);
        if (content is null) return false;
        var buffer = new byte[81920];
        var position = 0;
        int count;
        while ((count = await content.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (position > bytes.Length - count || !buffer.AsSpan(0, count).SequenceEqual(bytes.AsSpan(position, count))) return false;
            position += count;
        }
        return position == bytes.Length;
    }

    internal static Task DeleteUnreferencedAsync(IOperationsDbContext db, IFileStorage storage, IEnumerable<string> keys) =>
        FlightServiceFiles.DeleteUnreferencedAsync(db, storage, keys);

    private static string NormalizeName(string value)
    {
        var name = Path.GetFileName(value.Trim().Replace('\\', '/'));
        return name.Length <= 255 ? name : name[..255];
    }
}
