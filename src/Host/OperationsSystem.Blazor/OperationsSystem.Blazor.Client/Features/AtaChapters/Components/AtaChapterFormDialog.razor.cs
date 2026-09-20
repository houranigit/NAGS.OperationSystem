using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.AtaChapters.Components;

public partial class AtaChapterFormDialog
{
    [Parameter] public bool CategoryMode { get; set; }
    [Parameter] public Guid? RecordId { get; set; }
    private FormModel model = new();
    private IReadOnlyList<CategoryChoice> categories = [];
    private string? rowVersion, errorMessage;
    private bool isLoading = true, isSaving, loadFailed;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (!CategoryMode)
            {
                var available = new List<CategoryChoice>();
                for (var page = 1; ; page++)
                {
                    var result = await MasterData.GetAtaChapterCategoriesAsync(page, 100, sort: "name");
                    available.AddRange(result.Items.Select(item => new CategoryChoice(item.Id, item.IsActive ? item.Name : $"{item.Name} (inactive)")));
                    if (available.Count >= result.TotalCount || result.Items.Count == 0) break;
                }
                categories = available;
            }
            if (RecordId is { } id)
            {
                if (CategoryMode)
                {
                    var item = await MasterData.GetAtaChapterCategoryAsync(id);
                    model.Name = item.Name;
                    rowVersion = item.RowVersion;
                }
                else
                {
                    var item = await MasterData.GetAtaChapterAsync(id);
                    model = new FormModel { CategoryId = item.CategoryId, Code = item.Code, Title = item.Title };
                    rowVersion = item.RowVersion;
                }
            }
        }
        catch (ApiException ex)
        {
            loadFailed = true;
            errorMessage = ex.ToDisplayMessage("Could not load ATA chapter data. Close and try again.");
        }
        finally { isLoading = false; }
    }

    private async Task SaveAsync()
    {
        if (loadFailed) return;
        errorMessage = null;
        if (CategoryMode ? string.IsNullOrWhiteSpace(model.Name) :
            model.CategoryId is null || string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Title))
        {
            errorMessage = CategoryMode ? "Enter a category name." : "Select a category and enter the chapter number and title.";
            return;
        }
        isSaving = true;
        try
        {
            if (CategoryMode)
            {
                var request = new SaveAtaChapterCategoryRequest(model.Name.Trim());
                if (RecordId is { } id) await MasterData.UpdateAtaChapterCategoryAsync(id, request, rowVersion!);
                else await MasterData.CreateAtaChapterCategoryAsync(request);
            }
            else
            {
                var request = new SaveAtaChapterRequest(model.CategoryId!.Value, model.Code.Trim(), model.Title.Trim());
                if (RecordId is { } id) await MasterData.UpdateAtaChapterAsync(id, request, rowVersion!);
                else await MasterData.CreateAtaChapterAsync(request);
            }
            DialogService.Close(true);
        }
        catch (ApiException ex) { errorMessage = ex.ToDisplayMessage("Could not save ATA chapter data."); }
        finally { isSaving = false; }
    }

    private sealed class FormModel
    {
        public string Name { get; set; } = string.Empty;
        public Guid? CategoryId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }
    private sealed record CategoryChoice(Guid Id, string Label);
}
