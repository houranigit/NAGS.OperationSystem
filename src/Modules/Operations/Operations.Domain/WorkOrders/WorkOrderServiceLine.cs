using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

/// <summary>A performed service on a work order, tagged Planned or Extra, with a time window and one or more employees.</summary>
public sealed class WorkOrderServiceLine : Entity<Guid>
{
    private readonly List<WorkOrderServiceLineEmployee> _employees = [];

    private WorkOrderServiceLine() { }

    private WorkOrderServiceLine(Guid id, Guid workOrderId, ServiceSnapshot service, ServiceLineOrigin origin, TimeWindow window, string? description, bool returnToRamp)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Service = service;
        Origin = origin;
        Window = window;
        Description = description;
        ReturnToRamp = returnToRamp;
    }

    public Guid WorkOrderId { get; private set; }
    public ServiceSnapshot Service { get; private set; } = null!;
    public ServiceLineOrigin Origin { get; private set; }
    public TimeWindow Window { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool ReturnToRamp { get; private set; }

    public IReadOnlyList<WorkOrderServiceLineEmployee> Employees => _employees.AsReadOnly();

    internal static Result<WorkOrderServiceLine> Create(Guid workOrderId, ServiceLineInput input)
    {
        var performable = PerLandingPolicy.ValidatePerformedService(input.Service.ServiceId);
        if (performable.IsFailure)
            return performable.Error;

        var window = TimeWindow.Create(input.From, input.To);
        if (window.IsFailure)
            return window.Error;

        if (input.Employees.Count == 0)
            return Error.Validation("A service line must have at least one employee.", "Operations.ServiceLine.EmployeeRequired");

        if (!string.IsNullOrWhiteSpace(input.Description) && input.Description.Trim().Length > 1000)
            return Error.Validation("Service line description must be at most 1000 characters.", "Operations.ServiceLine.DescriptionTooLong");

        var line = new WorkOrderServiceLine(
            Guid.NewGuid(), workOrderId, input.Service, input.Origin, window.Value,
            string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(), input.ReturnToRamp);

        foreach (var employee in input.Employees.GroupBy(e => e.StaffMemberId).Select(g => g.First()))
            line._employees.Add(new WorkOrderServiceLineEmployee(Guid.NewGuid(), line.Id, employee));

        return line;
    }
}

/// <summary>A staff member who performed a work-order service line.</summary>
public sealed class WorkOrderServiceLineEmployee : Entity<Guid>
{
    private WorkOrderServiceLineEmployee() { }

    internal WorkOrderServiceLineEmployee(Guid id, Guid serviceLineId, StaffMemberSnapshot employee)
    {
        Id = id;
        ServiceLineId = serviceLineId;
        Employee = employee;
    }

    public Guid ServiceLineId { get; private set; }
    public StaffMemberSnapshot Employee { get; private set; } = null!;
}

/// <summary>Validated input for a performed service line.</summary>
public sealed record ServiceLineInput(
    ServiceSnapshot Service,
    ServiceLineOrigin Origin,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Description,
    bool ReturnToRamp,
    IReadOnlyList<StaffMemberSnapshot> Employees);
