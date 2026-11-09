using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

public sealed record FlightServicePerformerInput(StaffMemberSnapshot? Employee, TimeWindow? Window);

public sealed record FlightTaskPerformerInput(StaffMemberSnapshot? Employee, TimeWindow? Window, string? Notes = null);
