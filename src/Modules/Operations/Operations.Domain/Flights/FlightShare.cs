using BuildingBlocks.Domain.Results;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>An immutable discovery post. Sharing never assigns employees or grants operational authority.</summary>
public sealed class FlightShare
{
    private readonly List<FlightShareRecipient> _recipients = [];
    private FlightShare() { }

    public Guid Id { get; private set; }
    public Guid FlightId { get; private set; }
    public Guid StationId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public Guid AuthorStaffMemberId { get; private set; }
    public string AuthorFullName { get; private set; } = string.Empty;
    public string AuthorEmployeeId { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<FlightShareRecipient> Recipients => _recipients;

    public static Result<FlightShare> Create(Guid id, Guid flightId, Guid stationId, Guid authorUserId,
        StaffMemberSnapshot author, string? message, IReadOnlyCollection<StaffMemberSnapshot> recipients, DateTimeOffset now)
    {
        message = NormalizeMessage(message);
        if (id == Guid.Empty || flightId == Guid.Empty || stationId == Guid.Empty || authorUserId == Guid.Empty ||
            author is null || author.StaffMemberId == Guid.Empty || now == default || recipients is null ||
            recipients.Any(item => item is null || item.StaffMemberId == Guid.Empty) || message?.Length > 2000)
            return Error.Validation("A valid share, author, recipients and message of at most 2,000 characters are required.", "Operations.FlightShare.Invalid");
        var share = new FlightShare
        {
            Id = id, FlightId = flightId, StationId = stationId, AuthorUserId = authorUserId,
            AuthorStaffMemberId = author.StaffMemberId, AuthorFullName = author.FullName,
            AuthorEmployeeId = author.EmployeeId, Message = message, CreatedAtUtc = now.ToUniversalTime()
        };
        share._recipients.AddRange(recipients.DistinctBy(item => item.StaffMemberId)
            .Select(item => new FlightShareRecipient(id, item)));
        return share;
    }

    public bool Matches(Guid flightId, Guid stationId, Guid userId, string? message, IReadOnlyCollection<Guid> staffIds) =>
        FlightId == flightId && StationId == stationId && AuthorUserId == userId && Message == NormalizeMessage(message) &&
        _recipients.Select(item => item.StaffMemberId).ToHashSet().SetEquals(staffIds);

    public static string? NormalizeMessage(string? message) => string.IsNullOrWhiteSpace(message) ? null : message.Trim();
}

public sealed class FlightShareRecipient
{
    private FlightShareRecipient() { }
    internal FlightShareRecipient(Guid shareId, StaffMemberSnapshot employee)
    {
        ShareId = shareId; StaffMemberId = employee.StaffMemberId;
        FullName = employee.FullName; EmployeeId = employee.EmployeeId;
    }

    public Guid ShareId { get; private set; }
    public Guid StaffMemberId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string EmployeeId { get; private set; } = string.Empty;
}
