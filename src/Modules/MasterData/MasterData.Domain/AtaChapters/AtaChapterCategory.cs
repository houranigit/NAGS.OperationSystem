using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.AtaChapters;

/// <summary>ATA chapter category catalog item with active/inactive lifecycle.</summary>
public sealed class AtaChapterCategory : AggregateRoot<Guid>
{
    private AtaChapterCategory() { }

    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<AtaChapterCategory> Create(string? name, DateTimeOffset now, Guid? id = null)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        return new AtaChapterCategory
        {
            Id = id ?? Guid.NewGuid(),
            Name = nameCheck.Value,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public Result Update(string? name, DateTimeOffset now)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        Name = nameCheck.Value;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Activate(DateTimeOffset now)
    {
        if (IsActive)
            return Result.Success();

        IsActive = true;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return Result.Success();

        IsActive = false;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("ATA chapter category name is required.", "MasterData.AtaChapterCategory.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
            return Error.Validation("ATA chapter category name must be at most 100 characters.", "MasterData.AtaChapterCategory.NameTooLong");

        return trimmed;
    }

}
