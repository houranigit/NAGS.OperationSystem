using System.Reflection;
using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.AtaChapters.Pages;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Pages;

public sealed class AtaChaptersPageTests
{
    [Fact]
    public async Task Opening_page_loads_chapters_and_categories_without_a_grid_interaction()
    {
        var runtime = new CatalogJsRuntime();
        var page = CreatePage(runtime);

        await page.InitializeAsync();

        runtime.Requests.ShouldBe(new[]
        {
            "/masterdata/ata-chapters?page=1&pageSize=25",
            "/masterdata/ata-chapter-categories?page=1&pageSize=10"
        }, ignoreOrder: true);
        page.Read<IReadOnlyList<AtaChapterModel>>("chapters").ShouldHaveSingleItem().ShouldBe(runtime.Chapter);
        page.Read<IReadOnlyList<AtaChapterCategoryModel>>("categories").ShouldHaveSingleItem().ShouldBe(runtime.Category);
        page.Read<int>("chapterCount").ShouldBe(66);
        page.Read<int>("categoryCount").ShouldBe(5);
        page.Read<bool>("chapterError").ShouldBeFalse();
        page.Read<bool>("categoryError").ShouldBeFalse();
        page.Read<bool>("loadingChapters").ShouldBeFalse();
        page.Read<bool>("loadingCategories").ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_load_failure_does_not_hide_the_other_catalog(bool failCategories)
    {
        var runtime = new CatalogJsRuntime(failCategories ? "ata-chapter-categories" : "ata-chapters");
        var page = CreatePage(runtime);

        await page.InitializeAsync();

        runtime.Requests.Count.ShouldBe(2);
        page.Read<bool>("chapterError").ShouldBe(!failCategories);
        page.Read<bool>("categoryError").ShouldBe(failCategories);
        page.Read<IReadOnlyList<AtaChapterModel>>("chapters").Count.ShouldBe(failCategories ? 1 : 0);
        page.Read<IReadOnlyList<AtaChapterCategoryModel>>("categories").Count.ShouldBe(failCategories ? 0 : 1);
        page.Read<bool>("loadingChapters").ShouldBeFalse();
        page.Read<bool>("loadingCategories").ShouldBeFalse();
    }

    private static PageHarness CreatePage(IJSRuntime runtime)
    {
        var tokens = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokens, locale);
        var api = new BrowserApiClient(runtime, tokens, locale, refresher);
        var page = new PageHarness();
        typeof(AtaChaptersPage).GetProperty("MasterData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new MasterDataApiClient(api));
        return page;
    }

    private sealed class PageHarness : AtaChaptersPage
    {
        public Task InitializeAsync() => base.OnInitializedAsync();

        public T Read<T>(string name) =>
            (T)typeof(AtaChaptersPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
    }

    private sealed class CatalogJsRuntime(string? failingCatalog = null) : IJSRuntime
    {
        public List<string> Requests { get; } = [];
        public AtaChapterCategoryModel Category { get; } = new(Guid.NewGuid(), "Airframe Systems (20s-50s)",
            true, DateTimeOffset.Parse("2026-09-20T10:00:00Z"), null, "category-version");
        public AtaChapterModel Chapter => new(Guid.Parse("995d7612-7b09-4dc7-b646-74bcb70b1736"), Category.Id,
            Category.Name, "21", "Air Conditioning", true, true, Category.CreatedAtUtc, null, "chapter-version");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.api.request");
            args![0].ShouldBe("GET");
            var path = (string)args[1]!;
            Requests.Add(path);
            if (failingCatalog is not null && path.StartsWith($"/masterdata/{failingCatalog}?", StringComparison.Ordinal))
                throw new JSException(JsonSerializer.Serialize(new { status = 500, body = "Catalog unavailable" }));

            var response = path.StartsWith("/masterdata/ata-chapter-categories?", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new PagedResult<AtaChapterCategoryModel>([Category], 1, 10, 5))
                : JsonSerializer.Serialize(new PagedResult<AtaChapterModel>([Chapter], 1, 25, 66));
            return ValueTask.FromResult((TValue)(object)response);
        }
    }
}
