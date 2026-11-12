using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

public sealed record FlightServicePerformerInput(StaffMemberSnapshot? Employee, TimeWindow? Window, Guid? Id = null);

public sealed record FlightTaskPerformerInput(StaffMemberSnapshot? Employee, TimeWindow? Window, string? Notes = null, Guid? Id = null);

public sealed record FlightTaskToolInput(ToolSnapshot? Tool, ResourceUsage? Usage, string? Notes = null, Guid? Id = null);
public sealed record FlightTaskMaterialInput(MaterialSnapshot? Material, ResourceUsage? Usage, string? Notes = null, Guid? Id = null);
public sealed record FlightTaskGeneralSupportInput(GeneralSupportSnapshot? GeneralSupport, ResourceUsage? Usage, string? Notes = null, Guid? Id = null);
