namespace MasterData.Application.Contracts;

public sealed record AtaChapterCategoryDto(Guid Id, string Name, bool IsActive,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);

public sealed record AtaChapterDto(Guid Id, Guid CategoryId, string CategoryName, string Code, string Title,
    bool IsActive, bool CategoryIsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);
