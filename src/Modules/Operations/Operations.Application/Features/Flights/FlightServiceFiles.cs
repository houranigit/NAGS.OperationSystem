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

public sealed record GetMobileFlightServiceAttachmentQuery(Guid FlightId, Guid RecordId, Guid AttachmentId, DateTimeOffset ExpectedUploadedAt)
    : IQuery<FlightServiceAttachmentContent>;
public sealed record FlightServiceAttachmentContent(Stream Content, string ContentType, string FileName);

public sealed class GetMobileFlightServiceAttachmentQueryHandler(IOperationsDbContext db, IMobileFlightScope scope, IFileStorage storage)
    : IQueryHandler<GetMobileFlightServiceAttachmentQuery, FlightServiceAttachmentContent>
{
    public async Task<Result<FlightServiceAttachmentContent>> Handle(GetMobileFlightServiceAttachmentQuery request, CancellationToken cancellationToken)
    {
        var actor = await scope.ResolveAsync(cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var flight = await MobileFlightQuery.ApplyScope(db.Flights.AsNoTracking(), actor.Value)
            .Include(f => f.ServiceRecords).ThenInclude(s => s.Attachments)
            .SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        var attachment = flight?.Services.SingleOrDefault(s => s.Id == request.RecordId)?.Attachments.SingleOrDefault(a => a.Id == request.AttachmentId);
        if (attachment is null) return NotFound();
        if (attachment.UploadedAtUtc != request.ExpectedUploadedAt)
            return Error.PreconditionFailed("The attachment changed. Reload the flight and retry.", "Operations.FlightService.AttachmentChanged");
        var content = await storage.OpenAsync(attachment.Details.StorageKey, cancellationToken);
        return content is null ? NotFound() : new FlightServiceAttachmentContent(content, attachment.Details.ContentType, attachment.Details.FileName);
    }
    private static Error NotFound() => Error.NotFound("Service attachment not found.", "Operations.FlightService.AttachmentNotFound");
}

internal static class FlightServiceFiles
{
    internal static async Task<bool> MatchesAsync(IFileStorage storage, FlightService service,
        IReadOnlyList<SaveFlightServiceAttachmentRequest>? desired, CancellationToken cancellationToken)
    {
        if (desired is null) return true;
        if (desired.Count != service.Attachments.Count) return false;
        foreach (var input in desired)
        {
            var existing = service.Attachments.SingleOrDefault(a => a.Id == input.Id);
            if (existing is null || existing.Details.Kind != (AttachmentKind)input.Kind ||
                existing.Details.FileName != NormalizeName(input.FileName) || !existing.Details.ContentType.Equals(input.ContentType.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            if (input.Content is not null && !await ContentMatchesAsync(storage, existing, input.Content, cancellationToken)) return false;
        }
        return true;
    }

    internal static async Task<bool> ContentMatchesAsync(IFileStorage storage, FlightServiceAttachment existing, byte[] bytes, CancellationToken cancellationToken)
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

    internal static async Task DeleteUnreferencedAsync(IOperationsDbContext db, IFileStorage storage, IEnumerable<string> keys)
    {
        foreach (var key in keys.Distinct())
        {
            try
            {
                // Check persisted references, including ambiguous commits; tracked unsaved intent is not durable ownership.
                var referenced = await db.Flights.AsNoTracking().AnyAsync(f =>
                    f.ServiceRecords.Any(s => s.Attachments.Any(a => a.Details.StorageKey == key)) ||
                    f.TaskRecords.Any(t => t.Attachments.Any(a => a.Details.StorageKey == key)) ||
                    f.DepartureSubmissions.Any(d => d.Details.CustomerSignature.StorageKey == key) ||
                    f.ReturnToRamps.Any(r => r.DepartureDetails != null && r.DepartureDetails.CustomerSignature != null &&
                        r.DepartureDetails.CustomerSignature.StorageKey == key), CancellationToken.None);
                if (!referenced) await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch
            {
                // Cleanup must neither mask the original failure nor turn an accepted write into a reported failure.
            }
        }
    }
    private static string NormalizeName(string value)
    {
        var name = Path.GetFileName(value.Trim().Replace('\\', '/'));
        return name.Length <= 255 ? name : name[..255];
    }
}
