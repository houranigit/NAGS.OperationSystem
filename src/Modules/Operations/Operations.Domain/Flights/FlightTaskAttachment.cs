using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A file reference owned by one flight task.</summary>
public sealed class FlightTaskAttachment : Entity<Guid>
{
    private FlightTaskAttachment() { }

    internal FlightTaskAttachment(Guid id, Guid flightTaskId, AttachmentDetails details, Guid uploadedByUserId, DateTimeOffset now)
    {
        Id = id;
        FlightTaskId = flightTaskId;
        Details = details.Copy();
        UploadedByUserId = uploadedByUserId;
        UploadedAtUtc = now;
    }

    public Guid FlightTaskId { get; private set; }
    public AttachmentDetails Details { get; private set; } = null!;
    public Guid UploadedByUserId { get; private set; }
    public DateTimeOffset UploadedAtUtc { get; private set; }
}
