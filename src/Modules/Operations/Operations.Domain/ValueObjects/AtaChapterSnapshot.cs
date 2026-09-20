using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>The chapter recorded on a task, retained when its catalog entry changes.</summary>
public sealed class AtaChapterSnapshot : ValueObject
{
    private AtaChapterSnapshot() { }

    public AtaChapterSnapshot(Guid ataChapterId, string code, string title)
    {
        AtaChapterId = ataChapterId;
        Code = code;
        Title = title;
    }

    public Guid AtaChapterId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return AtaChapterId;
        yield return Code;
        yield return Title;
    }
}
