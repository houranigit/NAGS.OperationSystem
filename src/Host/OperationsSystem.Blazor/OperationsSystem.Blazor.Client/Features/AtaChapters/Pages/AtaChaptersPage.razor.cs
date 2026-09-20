using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.AtaChapters.Components;
using OperationsSystem.Blazor.Client.Shared;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.AtaChapters.Pages;

public partial class AtaChaptersPage
{
    private DataListCard<AtaChapterModel>? chapterList;
    private DataListCard<AtaChapterCategoryModel>? categoryList;
    private IReadOnlyList<AtaChapterModel> chapters = [];
    private IReadOnlyList<AtaChapterCategoryModel> categories = [];
    private int chapterCount, categoryCount;
    private int chapterPage = 1, categoryPage = 1, chapterPageSize = 25, categoryPageSize = 10;
    private bool loadingChapters, loadingCategories, chapterError, categoryError;
    private string chapterSearch = string.Empty, categorySearch = string.Empty;
    private string? chapterSort, categorySort;
    private bool? chapterStatus, categoryStatus;
    private readonly IReadOnlyList<StatusOption> statuses = [new("Active", true), new("Inactive", false)];

    protected override async Task OnInitializedAsync()
    {
        await Task.WhenAll(FetchChaptersAsync(), FetchCategoriesAsync());
    }

    private async Task LoadChaptersAsync(LoadDataArgs args)
    {
        chapterPageSize = args.Top ?? chapterPageSize;
        chapterPage = (args.Skip ?? 0) / Math.Max(chapterPageSize, 1) + 1;
        chapterSort = SortBuilder.From(args);
        await FetchChaptersAsync();
    }

    private async Task LoadCategoriesAsync(LoadDataArgs args)
    {
        categoryPageSize = args.Top ?? categoryPageSize;
        categoryPage = (args.Skip ?? 0) / Math.Max(categoryPageSize, 1) + 1;
        categorySort = SortBuilder.From(args);
        await FetchCategoriesAsync();
    }

    private async Task FetchChaptersAsync()
    {
        loadingChapters = true;
        chapterError = false;
        try
        {
            var result = await MasterData.GetAtaChaptersAsync(chapterPage, chapterPageSize, chapterSearch, chapterStatus, sort: chapterSort);
            chapters = result.Items;
            chapterCount = (int)result.TotalCount;
        }
        catch (ApiException) { chapterError = true; }
        finally { loadingChapters = false; }
    }

    private async Task FetchCategoriesAsync()
    {
        loadingCategories = true;
        categoryError = false;
        try
        {
            var result = await MasterData.GetAtaChapterCategoriesAsync(categoryPage, categoryPageSize, categorySearch, categoryStatus, categorySort);
            categories = result.Items;
            categoryCount = (int)result.TotalCount;
        }
        catch (ApiException) { categoryError = true; }
        finally { loadingCategories = false; }
    }

    private async Task ReloadChaptersAsync()
    {
        chapterPage = 1;
        if (chapterList is not null) await chapterList.ReloadAsync();
        else await FetchChaptersAsync();
    }

    private async Task ReloadCategoriesAsync()
    {
        categoryPage = 1;
        if (categoryList is not null) await categoryList.ReloadAsync();
        else await FetchCategoriesAsync();
    }

    private async Task ChapterStatusChangedAsync(bool? value) { chapterStatus = value; await ReloadChaptersAsync(); }
    private async Task CategoryStatusChangedAsync(bool? value) { categoryStatus = value; await ReloadCategoriesAsync(); }

    private async Task EditAsync(bool category, Guid? id)
    {
        var result = await DialogService.OpenAsync<AtaChapterFormDialog>(
            $"{(id.HasValue ? "Edit" : "Add")} ATA {(category ? "category" : "chapter")}",
            new Dictionary<string, object?> { [nameof(AtaChapterFormDialog.CategoryMode)] = category, [nameof(AtaChapterFormDialog.RecordId)] = id },
            new DialogOptions { Width = "640px" });
        if (result is true)
        {
            if (category) await ReloadCategoriesAsync();
            await ReloadChaptersAsync();
        }
    }

    private async Task SetActiveAsync(bool category, Guid id, bool active, string rowVersion)
    {
        try
        {
            if (category) await MasterData.SetAtaChapterCategoryActiveAsync(id, active, rowVersion);
            else await MasterData.SetAtaChapterActiveAsync(id, active, rowVersion);
            Notifications.Notify(NotificationSeverity.Success, $"ATA {(category ? "category" : "chapter")} {(active ? "activated" : "deactivated")}.");
            if (category) await ReloadCategoriesAsync();
            await ReloadChaptersAsync();
        }
        catch (ApiException ex)
        {
            Notifications.Notify(NotificationSeverity.Error, ex.ToDisplayMessage("Could not update ATA status. Refresh the list and try again."));
        }
    }

    private sealed record StatusOption(string Label, bool? Value);
}
