using BuildingBlocks.Domain.Entities;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public sealed class WorkOrderServiceLine : Entity<Guid>
{
    private readonly List<WorkOrderServiceLinePerformer> _performedBy = [];

    private WorkOrderServiceLine() { }

    internal WorkOrderServiceLine(Guid id, Guid workOrderId, WorkOrderServiceLineInput input)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Service = input.Service;
        Window = input.Window;
        Description = NormalizeDescription(input.Description);
        IsReturnToRamp = input.IsReturnToRamp;

        foreach (var performer in input.PerformedBy.GroupBy(p => p.StaffMemberId).Select(group => group.First()))
        {
            _performedBy.Add(new WorkOrderServiceLinePerformer(
                Guid.NewGuid(),
                WorkOrderId,
                Id,
                performer));
        }
    }

    public Guid WorkOrderId { get; private set; }
    public ServiceSnapshot Service { get; private set; } = null!;
    public IReadOnlyList<WorkOrderServiceLinePerformer> PerformedBy => _performedBy.AsReadOnly();
    public TimeWindow Window { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsReturnToRamp { get; private set; }

    public bool IsAircraftPerLanding => Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService;

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
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
