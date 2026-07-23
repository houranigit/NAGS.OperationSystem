using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public sealed class WorkOrderServiceLine : Entity<Guid>
{
    public const int MaxAttachments = 10;

    private readonly List<WorkOrderServiceLinePerformer> _performedBy = [];
    private readonly List<WorkOrderServiceLineAttachment> _attachments = [];

    private WorkOrderServiceLine() { }

    internal WorkOrderServiceLine(Guid id, Guid workOrderId, WorkOrderServiceLineInput input)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Apply(input);
    }

    public Guid WorkOrderId { get; private set; }
    public ServiceSnapshot Service { get; private set; } = null!;
    public IReadOnlyList<WorkOrderServiceLinePerformer> PerformedBy => _performedBy.AsReadOnly();
    public TimeWindow Window { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsReturnToRamp { get; private set; }
    public IReadOnlyList<WorkOrderServiceLineAttachment> Attachments => _attachments.AsReadOnly();

    public bool IsAircraftPerLanding => Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService;

    internal void Update(WorkOrderServiceLineInput input) => Apply(input);

    internal Result<WorkOrderServiceLineAttachment> AddAttachment(
        TaskAttachmentKind kind,
        string storageReference,
        string originalFileName,
        string contentType,
        long size)
    {
        if (_attachments.Count >= MaxAttachments)
            return Error.Validation("A service can have at most 10 attachments.", "Operations.WorkOrder.AttachmentLimitExceeded");
        if (string.IsNullOrWhiteSpace(storageReference))
            return Error.Validation("Attachment storage reference is required.", "Operations.WorkOrder.AttachmentStorageRequired");
        if (string.IsNullOrWhiteSpace(originalFileName))
            return Error.Validation("Attachment file name is required.", "Operations.WorkOrder.AttachmentFileNameRequired");
        if (string.IsNullOrWhiteSpace(contentType))
            return Error.Validation("Attachment content type is required.", "Operations.WorkOrder.AttachmentContentTypeRequired");
        if (size <= 0)
            return Error.Validation("Attachment file is empty.", "Operations.WorkOrder.AttachmentEmpty");

        var attachment = new WorkOrderServiceLineAttachment(
            WorkOrderId,
            Id,
            kind,
            storageReference.Trim(),
            TrimFileName(originalFileName),
            contentType.Trim(),
            size);
        _attachments.Add(attachment);
        return attachment;
    }

    internal Result<string> RemoveAttachment(Guid attachmentId)
    {
        var attachment = _attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null)
            return Error.NotFound("Attachment not found.", "Operations.WorkOrder.AttachmentNotFound");

        _attachments.Remove(attachment);
        return attachment.StorageReference;
    }

    private void Apply(WorkOrderServiceLineInput input)
    {
        Service = input.Service;
        Window = input.Window;
        Description = NormalizeDescription(input.Description);
        IsReturnToRamp = input.IsReturnToRamp;

        _performedBy.Clear();
        foreach (var performer in input.PerformedBy.GroupBy(p => p.StaffMemberId).Select(group => group.First()))
        {
            _performedBy.Add(new WorkOrderServiceLinePerformer(
                Guid.NewGuid(),
                WorkOrderId,
                Id,
                performer));
        }
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static string TrimFileName(string fileName)
    {
        var trimmed = Path.GetFileName(fileName.Trim());
        return trimmed.Length <= 255 ? trimmed : trimmed[..255];
    }
}

public sealed class WorkOrderServiceLinePerformer : Entity<Guid>
{
    private WorkOrderServiceLinePerformer() { }

    internal WorkOrderServiceLinePerformer(
        Guid id,
        Guid workOrderId,
        Guid workOrderServiceLineId,
        StaffMemberSnapshot staffMember)
    {
        Id = id;
        WorkOrderId = workOrderId;
        WorkOrderServiceLineId = workOrderServiceLineId;
        StaffMember = staffMember;
    }

    public Guid WorkOrderId { get; private set; }
    public Guid WorkOrderServiceLineId { get; private set; }
    public StaffMemberSnapshot StaffMember { get; private set; } = null!;
}

public sealed class WorkOrderServiceLineAttachment : Entity<Guid>
{
    private WorkOrderServiceLineAttachment() { }

    public WorkOrderServiceLineAttachment(
        Guid workOrderId,
        Guid workOrderServiceLineId,
        TaskAttachmentKind kind,
        string storageReference,
        string originalFileName,
        string contentType,
        long size)
    {
        Id = Guid.NewGuid();
        WorkOrderId = workOrderId;
        WorkOrderServiceLineId = workOrderServiceLineId;
        Kind = kind;
        StorageReference = storageReference;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        Size = size;
    }

    public Guid WorkOrderId { get; private set; }
    public Guid WorkOrderServiceLineId { get; private set; }
    public TaskAttachmentKind Kind { get; private set; }
    public string StorageReference { get; private set; } = null!;
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long Size { get; private set; }
}
