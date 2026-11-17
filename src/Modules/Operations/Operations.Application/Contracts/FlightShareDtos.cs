namespace Operations.Application.Contracts;

public sealed record PublishFlightShareRequest(string? Message = null, IReadOnlyList<Guid>? StaffMemberIds = null);

public sealed record FlightSharePostDto(Guid Id, Guid FlightId, Guid AuthorStaffMemberId, string AuthorFullName,
    string AuthorEmployeeId, string? Message, DateTimeOffset CreatedAtUtc, bool IsStationWide,
    IReadOnlyList<AssignedEmployeeDto> Recipients, MobileFlightListItemDto Flight, string? StationName = null);

public sealed record MobileFlightSharePageDto(IReadOnlyList<FlightSharePostDto> Items, int Page, int PageSize,
    long TotalCount, Guid StationId, DateTimeOffset AsOfUtc, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc)
{
    public int TotalPages => PageSize <= 0 || TotalCount <= 0 ? 0 :
        (int)Math.Min(int.MaxValue, ((TotalCount - 1) / PageSize) + 1);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
