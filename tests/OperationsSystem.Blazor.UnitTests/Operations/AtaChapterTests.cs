using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class AtaChapterTests
{
    [Fact]
    public void New_task_choices_require_both_chapter_and_parent_to_be_active()
    {
        var active = Chapter("21", "Air Conditioning");
        var disabled = Chapter("22", "Auto Flight") with { IsActive = false };
        var disabledCategory = Chapter("61", "Propellers") with { CategoryIsActive = false };

        var choices = AtaChapterChoices.Build([disabledCategory, disabled, active], null, null, null, null);

        choices.ShouldHaveSingleItem().ShouldBe(new AtaChapterChoice(active.Id, "21. Air Conditioning"));
    }

    [Fact]
    public void Unchanged_existing_selection_uses_its_snapshot_after_the_active_catalog_is_renamed()
    {
        var renamed = Chapter("021", "Updated air conditioning title");
        var other = Chapter("22", "Auto Flight");

        var unchanged = AtaChapterChoices.Build([renamed, other], renamed.Id, renamed.Id, "21", "Air Conditioning");
        unchanged.Single(item => item.Id == renamed.Id).Label.ShouldBe("21. Air Conditioning");
        unchanged.Single(item => item.Id == other.Id).Label.ShouldBe("22. Auto Flight");

        var changed = AtaChapterChoices.Build([renamed, other], other.Id, renamed.Id, "21", "Air Conditioning");
        changed.Single(item => item.Id == renamed.Id).Label.ShouldBe("021. Updated air conditioning title");
        changed.Single(item => item.Id == other.Id).Label.ShouldBe("22. Auto Flight");

        var newTask = AtaChapterChoices.Build([renamed], renamed.Id, null, null, null);
        newTask.ShouldHaveSingleItem().Label.ShouldBe("021. Updated air conditioning title");
    }

    [Fact]
    public void Existing_disabled_chapter_remains_readable_but_cannot_be_newly_selected()
    {
        var id = Guid.NewGuid();
        var choices = AtaChapterChoices.Build([], id, id, "61", "Propellers");

        choices.ShouldHaveSingleItem().ShouldBe(new AtaChapterChoice(id, "61. Propellers (inactive)", true));
        AtaChapterChoices.Build([], null, id, "61", "Propellers").ShouldBeEmpty();
        AtaChapterChoices.Build([], id, Guid.NewGuid(), "61", "Propellers").ShouldBeEmpty();
    }

    [Fact]
    public void Rtr_edit_clone_and_submit_preserve_chapter_identity_and_historical_label()
    {
        var task = TaskWithChapter();
        var occurrence = new WorkOrderReturnToRampModel(Guid.NewGuid(), task.FromUtc, task.ToUtc,
            null, Guid.NewGuid(), task.FromUtc, [], [task]);
        var timeZone = new UserTimeZone(new UnusedJsRuntime());

        var draft = ReturnToRampDraftMapper.FromModel(occurrence, timeZone).Clone();
        var edited = draft.Tasks.ShouldHaveSingleItem();
        edited.AtaChapterId.ShouldBe(task.AtaChapterId);
        edited.AtaChapterSnapshotId.ShouldBe(task.AtaChapterId);
        edited.AtaChapterCode.ShouldBe("21");
        edited.AtaChapterTitle.ShouldBe("Air Conditioning");
        ReturnToRampDraftMapper.ToRequest(draft, timeZone).Tasks.ShouldHaveSingleItem().AtaChapterId.ShouldBe(task.AtaChapterId);
    }

    [Fact]
    public void Merge_preserves_source_task_identity_to_resolve_an_inactive_chapter_snapshot()
    {
        var task = TaskWithChapter();
        var merged = WorkOrderMergeMapping.ToRequest(task);

        merged.Id.ShouldBe(task.Id);
        merged.AtaChapterId.ShouldBe(task.AtaChapterId);
    }

    [Fact]
    public void Legacy_task_without_chapter_remains_optional()
    {
        var task = TaskWithChapter() with { AtaChapterId = null, AtaChapterCode = null, AtaChapterTitle = null };
        WorkOrderMergeMapping.ToRequest(task).AtaChapterId.ShouldBeNull();
        AtaChapterChoices.Label(null, null).ShouldBe("None");
    }

    private static AtaChapterModel Chapter(string code, string title) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Aircraft Systems", code, title, true, true, DateTimeOffset.UtcNow, null, "version");

    private static WorkOrderTaskModel TaskWithChapter() => new(Guid.NewGuid(), "Minor", null,
        new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
        [], [], [], [], [], AtaChapterId: Guid.NewGuid(), AtaChapterCode: "21", AtaChapterTitle: "Air Conditioning");

    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }
}
