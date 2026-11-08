using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A file reference owned by one performed service.</summary>
public sealed class FlightServiceAttachment : Entity<Guid>
{
    private FlightServiceAttachment() { }

    internal FlightServiceAttachment(Guid id, Guid flightServiceId, AttachmentDetails details, Guid uploadedByUserId, DateTimeOffset now)
    {
        Id = id;
        FlightServiceId = flightServiceId;
        Details = details.Copy();
        UploadedByUserId = uploadedByUserId;
        UploadedAtUtc = now;
    }

    public Guid FlightServiceId { get; private set; }
    public AttachmentDetails Details { get; private set; } = null!;
    public Guid UploadedByUserId { get; private set; }
    public DateTimeOffset UploadedAtUtc { get; private set; }
}
