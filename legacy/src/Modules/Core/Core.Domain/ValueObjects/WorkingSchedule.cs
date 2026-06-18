using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.ValueObjects;

public sealed class WorkingSchedule : ValueObject
{
    private readonly IReadOnlySet<DayOfWeek> _days;

    public IReadOnlySet<DayOfWeek> Days => _days;

    // Stored as int bitmask: Sunday=1, Monday=2, Tuesday=4, Wednesday=8,
    //                        Thursday=16, Friday=32, Saturday=64
    public int Mask { get; }

    private WorkingSchedule(IReadOnlySet<DayOfWeek> days)
    {
        _days = days;
        Mask = days.Aggregate(0, (acc, d) => acc | (1 << (int)d));
    }

    public static Result<WorkingSchedule> Create(IEnumerable<DayOfWeek> days)
    {
        var set = days?.Distinct().ToHashSet();

        if (set is null || set.Count == 0)
            return Error.Validation("At least one working day is required.");

        return new WorkingSchedule(set);
    }

    public static WorkingSchedule FromMask(int mask)
    {
        var days = Enum.GetValues<DayOfWeek>()
            .Where(d => (mask & (1 << (int)d)) != 0)
            .ToHashSet();

        return new WorkingSchedule(days);
    }

    public bool Includes(DayOfWeek day) => _days.Contains(day);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Mask;
    }
}
