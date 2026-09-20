using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Application.Mobile;
using MasterData.Application.Contracts;
using MasterData.Application.Features.AtaChapters;
using MasterData.Contracts.Readers;
using MasterData.Infrastructure.Persistence;
using MasterData.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MasterData.IntegrationTests;

public sealed class AtaChapterApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Categories = MasterDataApiFactory.Base + "/ata-chapter-categories";
    private const string Chapters = MasterDataApiFactory.Base + "/ata-chapters";
    private sealed record Page<T>(List<T> Items, long TotalCount);

    [Fact]
    public async Task Pdf_baseline_has_all_66_chapters_and_only_propeller_rotor_seeds_inactive()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        AtaChapterSeedData.All.Count.ShouldBe(5);
        AtaChapterSeedData.All.Sum(x => x.Chapters.Count).ShouldBe(66);
        foreach (var categorySeed in AtaChapterSeedData.All)
        {
            var categoryId = MasterDataSeedIds.For("ata-chapter-category", categorySeed.Name);
            var category = await client.GetFromJsonAsync<AtaChapterCategoryDto>($"{Categories}/{categoryId}");
            category.ShouldNotBeNull();
            category.Name.ShouldBe(categorySeed.Name);
            category.IsActive.ShouldBe(categorySeed.Name != "Propeller / Rotor (60s)");
            var chapters = await client.GetFromJsonAsync<Page<AtaChapterDto>>($"{Chapters}?categoryId={categoryId}&pageSize=100");
            chapters.ShouldNotBeNull();
            chapters.TotalCount.ShouldBe(categorySeed.Chapters.Count);
            foreach (var chapterSeed in categorySeed.Chapters)
            {
                var chapter = chapters.Items.Single(x => x.Id == MasterDataSeedIds.For("ata-chapter", chapterSeed.Code));
                chapter.Code.ShouldBe(chapterSeed.Code);
                chapter.Title.ShouldBe(chapterSeed.Title);
                chapter.IsActive.ShouldBe(category.IsActive);
                chapter.CategoryIsActive.ShouldBe(category.IsActive);
            }
        }
        var options = await client.GetFromJsonAsync<List<AtaChapterDto>>($"{Chapters}/options");
        options.ShouldNotBeNull();
        options.ShouldContain(x => x.Code == "01-05");
        options.ShouldNotContain(x => !x.IsActive || !x.CategoryIsActive);
    }

    [Fact]
    public async Task Reseeding_preserves_admin_edits_codes_category_moves_and_statuses()
    {
        _ = await factory.CreateAuthenticatedAdminClientAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<MasterDataDataSeeder>();
        var category = await db.AtaChapterCategories.SingleAsync(x => x.Id == MasterDataSeedIds.For("ata-chapter-category", "General (00-19)"));
        var chapter = await db.AtaChapters.SingleAsync(x => x.Id == MasterDataSeedIds.For("ata-chapter", "00"));
        var categoryName = category.Name;
        var chapterCategory = chapter.CategoryId;
        var code = chapter.Code;
        var title = chapter.Title;
        try
        {
            category.Update("General admin edited", DateTimeOffset.UtcNow);
            category.Deactivate(DateTimeOffset.UtcNow);
            chapter.Update(MasterDataSeedIds.For("ata-chapter-category", "Structures (50s)"), "00-edited", "Admin title", DateTimeOffset.UtcNow);
            chapter.Deactivate(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            await seeder.SeedAsync();
            await seeder.SeedAsync();
            await db.Entry(category).ReloadAsync();
            await db.Entry(chapter).ReloadAsync();
            category.Name.ShouldBe("General admin edited");
            category.IsActive.ShouldBeFalse();
            chapter.Code.ShouldBe("00-edited");
            chapter.Title.ShouldBe("Admin title");
            chapter.CategoryId.ShouldBe(MasterDataSeedIds.For("ata-chapter-category", "Structures (50s)"));
            chapter.IsActive.ShouldBeFalse();
        }
        finally
        {
            category.Update(categoryName, DateTimeOffset.UtcNow);
            category.Activate(DateTimeOffset.UtcNow);
            chapter.Update(chapterCategory, code, title, DateTimeOffset.UtcNow);
            chapter.Activate(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Category_and_chapter_status_both_gate_task_options_and_mobile_reader()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var categoryId = await Create(client, Categories, new { name = $"Status {Guid.NewGuid():N}" });
        var chapterId = await Create(client, Chapters, new { categoryId, code = NewCode(), title = "Status chapter" });
        var category = (await client.GetFromJsonAsync<AtaChapterCategoryDto>($"{Categories}/{categoryId}"))!;
        await ChangeStatus(client, Categories, categoryId, "deactivate", category.RowVersion);
        await AssertUnavailable(chapterId, client);

        var chapter = (await client.GetFromJsonAsync<AtaChapterDto>($"{Chapters}/{chapterId}"))!;
        chapter.IsActive.ShouldBeTrue();
        chapter.CategoryIsActive.ShouldBeFalse();
        await ChangeStatus(client, Chapters, chapterId, "deactivate", chapter.RowVersion);
        category = (await client.GetFromJsonAsync<AtaChapterCategoryDto>($"{Categories}/{categoryId}"))!;
        await ChangeStatus(client, Categories, categoryId, "activate", category.RowVersion);
        await AssertUnavailable(chapterId, client);
        chapter = (await client.GetFromJsonAsync<AtaChapterDto>($"{Chapters}/{chapterId}"))!;
        await ChangeStatus(client, Chapters, chapterId, "activate", chapter.RowVersion);
        var options = (await client.GetFromJsonAsync<List<AtaChapterDto>>($"{Chapters}/options"))!;
        options.ShouldContain(x => x.Id == chapterId);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IMasterDataReader>().GetActiveAtaChaptersAsync(default))
            .ShouldContain(x => x.Id == chapterId);
    }

    [Fact]
    public async Task Editing_and_status_changes_require_current_row_versions()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var categoryId = await Create(client, Categories, new { name = $"Concurrency {Guid.NewGuid():N}" });
        var chapterId = await Create(client, Chapters, new { categoryId, code = NewCode(), title = "Before" });
        var chapter = (await client.GetFromJsonAsync<AtaChapterDto>($"{Chapters}/{chapterId}"))!;
        var body = new { categoryId, code = chapter.Code, title = "After" };
        (await client.PutAsJsonAsync($"{Chapters}/{chapterId}", body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendWithVersion(client, HttpMethod.Put, $"{Chapters}/{chapterId}", chapter.RowVersion, body)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendWithVersion(client, HttpMethod.Put, $"{Chapters}/{chapterId}", chapter.RowVersion, body)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendWithVersion(client, HttpMethod.Post, $"{Chapters}/{chapterId}/activate", chapter.RowVersion)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var category = (await client.GetFromJsonAsync<AtaChapterCategoryDto>($"{Categories}/{categoryId}"))!;
        (await SendWithVersion(client, HttpMethod.Put, $"{Categories}/{categoryId}", category.RowVersion, new { name = $"Renamed {Guid.NewGuid():N}" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendWithVersion(client, HttpMethod.Post, $"{Categories}/{categoryId}/deactivate", category.RowVersion)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Chapter_codes_are_unique_and_category_must_exist()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var categoryId = await Create(client, Categories, new { name = $"Validation {Guid.NewGuid():N}" });
        var code = NewCode();
        await Create(client, Chapters, new { categoryId, code, title = "Original" });
        (await client.PostAsJsonAsync(Chapters, new { categoryId, code, title = "Duplicate" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync(Chapters, new { categoryId = Guid.NewGuid(), code = NewCode(), title = "Missing category" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task All_category_and_chapter_mutations_refresh_mobile_catalogs()
    {
        _ = await factory.CreateAuthenticatedAdminClientAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
        var broadcaster = new CapturingBroadcaster();
        var time = TimeProvider.System;
        var categoryId = (await new CreateAtaChapterCategoryCommandHandler(db, time, broadcaster)
            .Handle(new CreateAtaChapterCategoryCommand($"Sync {Guid.NewGuid():N}"), default)).Value;
        var chapterId = (await new CreateAtaChapterCommandHandler(db, time, broadcaster)
            .Handle(new CreateAtaChapterCommand(categoryId, NewCode(), "Sync chapter"), default)).Value;
        var category = await db.AtaChapterCategories.SingleAsync(x => x.Id == categoryId);
        var chapter = await db.AtaChapters.SingleAsync(x => x.Id == chapterId);
        (await new UpdateAtaChapterCategoryCommandHandler(db, time, broadcaster)
            .Handle(new UpdateAtaChapterCategoryCommand(categoryId, $"Sync edited {Guid.NewGuid():N}", category.RowVersion), default)).IsSuccess.ShouldBeTrue();
        (await new UpdateAtaChapterCommandHandler(db, time, broadcaster)
            .Handle(new UpdateAtaChapterCommand(chapterId, categoryId, chapter.Code, "Edited", chapter.RowVersion), default)).IsSuccess.ShouldBeTrue();
        (await new DeactivateAtaChapterCategoryCommandHandler(db, time, broadcaster)
            .Handle(new DeactivateAtaChapterCategoryCommand(categoryId, category.RowVersion), default)).IsSuccess.ShouldBeTrue();
        (await new ActivateAtaChapterCategoryCommandHandler(db, time, broadcaster)
            .Handle(new ActivateAtaChapterCategoryCommand(categoryId, category.RowVersion), default)).IsSuccess.ShouldBeTrue();
        (await new DeactivateAtaChapterCommandHandler(db, time, broadcaster)
            .Handle(new DeactivateAtaChapterCommand(chapterId, chapter.RowVersion), default)).IsSuccess.ShouldBeTrue();
        (await new ActivateAtaChapterCommandHandler(db, time, broadcaster)
            .Handle(new ActivateAtaChapterCommand(chapterId, chapter.RowVersion), default)).IsSuccess.ShouldBeTrue();
        broadcaster.Changes.Count.ShouldBe(8);
        broadcaster.Changes.ShouldAllBe(x => x.Table == MobileSyncTables.AtaChapters && x.Op == MobileSyncOps.Refresh && x.Audience == MobileSyncAudience.AllStations);
    }

    [Fact]
    public async Task Ata_catalog_endpoints_require_authentication()
    {
        var client = factory.CreateClient();
        foreach (var url in new[] { Categories, Chapters, Chapters + "/options" })
            (await client.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync(Categories, new { name = "Forbidden" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task AssertUnavailable(Guid chapterId, HttpClient client)
    {
        (await client.GetFromJsonAsync<List<AtaChapterDto>>($"{Chapters}/options"))!.ShouldNotContain(x => x.Id == chapterId);
        (await client.GetFromJsonAsync<Page<AtaChapterDto>>($"{Chapters}?effectiveActive=true&pageSize=100"))!.Items.ShouldNotContain(x => x.Id == chapterId);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IMasterDataReader>().GetActiveAtaChaptersAsync(default))
            .ShouldNotContain(x => x.Id == chapterId);
    }

    private static string NewCode() => Guid.NewGuid().ToString("N")[..20];

    private static async Task<Guid> Create(HttpClient client, string route, object body)
    {
        var response = await client.PostAsJsonAsync(route, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task ChangeStatus(HttpClient client, string route, Guid id, string action, string rowVersion) =>
        (await SendWithVersion(client, HttpMethod.Post, $"{route}/{id}/{action}", rowVersion)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    private static Task<HttpResponseMessage> SendWithVersion(HttpClient client, HttpMethod method, string url, string rowVersion, object? body = null)
    {
        var message = new HttpRequestMessage(method, url);
        message.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        if (body is not null)
            message.Content = JsonContent.Create(body);
        return client.SendAsync(message);
    }

    private sealed class CapturingBroadcaster : IMobileSyncBroadcaster
    {
        public List<MobileSyncChange> Changes { get; } = [];
        public void Enqueue(MobileSyncChange change) => Changes.Add(change);
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
