using MasterData.Domain.AtaChapters;
using Shouldly;

namespace MasterData.Domain.UnitTests.Catalogs;

public sealed class AtaChapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Codes_preserve_leading_zeros_and_combined_pdf_chapters()
    {
        var chapter = AtaChapter.Create(Guid.NewGuid(), " 01-05 ", " Maintenance checks ", Now).Value;
        chapter.Code.ShouldBe("01-05");
        chapter.Title.ShouldBe("Maintenance checks");
        chapter.IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "Title")]
    [InlineData("00", " ")]
    public void Missing_code_or_title_is_rejected(string code, string title) =>
        AtaChapter.Create(Guid.NewGuid(), code, title, Now).IsFailure.ShouldBeTrue();

    [Fact]
    public void Chapter_requires_a_category_and_respects_snapshot_length_limits()
    {
        AtaChapter.Create(Guid.Empty, "00", "General", Now).IsFailure.ShouldBeTrue();
        AtaChapter.Create(Guid.NewGuid(), new string('0', 21), "General", Now).IsFailure.ShouldBeTrue();
        AtaChapter.Create(Guid.NewGuid(), "00", new string('x', 201), Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_edit_leaves_existing_chapter_intact()
    {
        var categoryId = Guid.NewGuid();
        var chapter = AtaChapter.Create(categoryId, "00", "General", Now).Value;
        chapter.Update(Guid.NewGuid(), "01", " ", Now.AddDays(1)).IsFailure.ShouldBeTrue();
        chapter.CategoryId.ShouldBe(categoryId);
        chapter.Code.ShouldBe("00");
        chapter.Title.ShouldBe("General");
    }

    [Fact]
    public void Category_and_chapter_can_be_edited_and_toggled_independently()
    {
        var category = AtaChapterCategory.Create("Category", Now).Value;
        var chapter = AtaChapter.Create(category.Id, "00", "General", Now).Value;
        category.Deactivate(Now.AddMinutes(1));
        chapter.IsActive.ShouldBeTrue();
        category.Update("Renamed", Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        category.IsActive.ShouldBeFalse();
        chapter.Deactivate(Now.AddMinutes(3));
        category.Activate(Now.AddMinutes(4));
        chapter.IsActive.ShouldBeFalse();
        chapter.Update(Guid.NewGuid(), "99", "Custom", Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        chapter.Activate(Now.AddMinutes(6));
        chapter.IsActive.ShouldBeTrue();
    }
}
