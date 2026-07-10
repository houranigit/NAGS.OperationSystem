using BuildingBlocks.Domain.Entities;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public sealed class WorkOrderServiceLine : Entity<Guid>
{
    private WorkOrderServiceLine() { }

    internal WorkOrderServiceLine(Guid id, Guid workOrderId, WorkOrderServiceLineInput input)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Service = input.Service;
        PerformedBy = input.PerformedBy;
        Window = input.Window;
        Description = NormalizeDescription(input.Description);
    }

    public Guid WorkOrderId { get; private set; }
    public ServiceSnapshot Service { get; private set; } = null!;
    public StaffMemberSnapshot PerformedBy { get; private set; } = null!;
    public TimeWindow Window { get; private set; } = null!;
    public string? Description { get; private set; }

    public bool IsAircraftPerLanding => Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService;

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
