using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.AtaChapters;

/// <summary>An editable ATA chapter. Both the chapter and its category must be active for new work.</summary>
public sealed class AtaChapter : AggregateRoot<Guid>
{
    private AtaChapter() { }

    public Guid CategoryId { get; private set; }
    public AtaChapterCategory Category { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<AtaChapter> Create(Guid categoryId, string? code, string? title, DateTimeOffset now, Guid? id = null)
    {
        var validation = Validate(categoryId, code, title);
        if (validation.IsFailure)
            return validation.Error;

        return new AtaChapter
        {
            Id = id ?? Guid.NewGuid(),
            CategoryId = categoryId,
            Code = code!.Trim(),
            Title = title!.Trim(),
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public Result Update(Guid categoryId, string? code, string? title, DateTimeOffset now)
    {
        var validation = Validate(categoryId, code, title);
        if (validation.IsFailure)
            return validation.Error;

        CategoryId = categoryId;
        Code = code!.Trim();
        Title = title!.Trim();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Activate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            IsActive = true;
            UpdatedAtUtc = now;
        }
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            IsActive = false;
            UpdatedAtUtc = now;
        }
        return Result.Success();
    }

    private static Result Validate(Guid categoryId, string? code, string? title)
    {
        if (categoryId == Guid.Empty)
            return Error.Validation("ATA chapter category is required.", "MasterData.AtaChapter.CategoryRequired");
        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation("ATA chapter code is required.", "MasterData.AtaChapter.CodeRequired");
        if (code.Trim().Length > 20)
            return Error.Validation("ATA chapter code must be at most 20 characters.", "MasterData.AtaChapter.CodeTooLong");
        if (string.IsNullOrWhiteSpace(title))
            return Error.Validation("ATA chapter title is required.", "MasterData.AtaChapter.TitleRequired");
        if (title.Trim().Length > 200)
            return Error.Validation("ATA chapter title must be at most 200 characters.", "MasterData.AtaChapter.TitleTooLong");
        return Result.Success();
    }
}
