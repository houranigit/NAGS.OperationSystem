using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace MasterData.Domain.StaffMembers;

/// <summary>
/// A staff member's working days. Optional; when present at least one day is required. Persisted as a
/// seven-bit mask (bit 0 = Sunday … bit 6 = Saturday). Does not model shift start/end times in v1.
/// </summary>
public sealed class WorkingSchedule : ValueObject
{
    private readonly SortedSet<DayOfWeek> _days;

    private WorkingSchedule(SortedSet<DayOfWeek> days) => _days = days;

    public IReadOnlyCollection<DayOfWeek> Days => _days;

    public static Result<WorkingSchedule> Create(IEnumerable<DayOfWeek>? days)
    {
        if (days is null)
            return Error.Validation("A working schedule requires at least one day.", "MasterData.WorkingSchedule.Empty");

        var set = new SortedSet<DayOfWeek>(days);
        if (set.Count == 0)
            return Error.Validation("A working schedule requires at least one day.", "MasterData.WorkingSchedule.Empty");

        return new WorkingSchedule(set);
    }

    public int ToMask()
    {
        var mask = 0;
        foreach (var day in _days)
            mask |= 1 << (int)day;
        return mask;
    }

    public static WorkingSchedule FromMask(int mask)
    {
        var set = new SortedSet<DayOfWeek>();
        for (var day = 0; day <= 6; day++)
        {
            if ((mask & (1 << day)) != 0)
                set.Add((DayOfWeek)day);
        }
        return new WorkingSchedule(set);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var day in _days)
            yield return day;
    }
}
