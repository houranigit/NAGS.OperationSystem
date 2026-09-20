using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

internal sealed record AtaChapterChoice(Guid Id, string Label, bool IsDisabled = false);

internal static class AtaChapterChoices
{
    internal static string Label(string? code, string? title) =>
        string.IsNullOrWhiteSpace(code) ? title ?? "None" : $"{code}. {title}";

    internal static IReadOnlyList<AtaChapterChoice> Build(IReadOnlyList<AtaChapterModel> options,
        Guid? selectedId, Guid? snapshotId, string? snapshotCode, string? snapshotTitle)
    {
        var choices = options.Where(item => item.IsActive && item.CategoryIsActive)
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(item => new AtaChapterChoice(item.Id,
                item.Id == selectedId && item.Id == snapshotId
                    ? Label(snapshotCode, snapshotTitle)
                    : item.Label)).ToList();
        if (selectedId is { } id && id == snapshotId && choices.All(item => item.Id != id))
            choices.Add(new AtaChapterChoice(id, $"{Label(snapshotCode, snapshotTitle)} (inactive)", true));
        return choices;
    }
}
