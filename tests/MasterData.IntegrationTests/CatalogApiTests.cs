using System.Net;
using System.Net.Http.Json;
using MasterData.Contracts.Seeding;
using Shouldly;

namespace MasterData.IntegrationTests;

public class CatalogApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record CatalogDetail(Guid Id, string Name, string? Description, bool IsActive, bool IsSystem,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);
    private sealed record CatalogItem(Guid Id, string Name, string? Description, bool IsActive, bool IsSystem);
    private sealed record AircraftTypeDetail(Guid Id, string Manufacturer, string Model, string? Notes, bool IsActive,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);
    private sealed record AircraftTypeItem(Guid Id, string Manufacturer, string Model, string? Notes, bool IsActive);
    private sealed record ToolDetail(Guid Id, string Name, string? Description, bool IsActive,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion, List<ToolEquipment> Equipments);
    private sealed record ToolEquipment(Guid Id, string FactoryId, string SerialId, DateOnly? CalibrationDate);

    [Fact]
    public async Task Seeded_operation_and_service_catalogs_are_available_with_expected_names()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();

        var adHoc = await client.GetFromJsonAsync<CatalogDetail>($"{Base}/operation-types/{WellKnownMasterDataIds.AdHocOperationType}");
        var aircraftPerLanding = await client.GetFromJsonAsync<CatalogDetail>($"{Base}/services/{WellKnownMasterDataIds.AircraftPerLandingService}");
        var onCall = await client.GetFromJsonAsync<CatalogDetail>($"{Base}/services/{WellKnownMasterDataIds.OnCallService}");

        adHoc!.Name.ShouldBe("Ad Hoc");
        adHoc.IsSystem.ShouldBeTrue();
        aircraftPerLanding!.Name.ShouldBe("Aircraft Per Landing");
        aircraftPerLanding.IsSystem.ShouldBeTrue();
        onCall!.Name.ShouldBe("On Call");
        onCall.IsSystem.ShouldBeTrue();
    }

    public static IEnumerable<object[]> SeededCatalogRoutes()
    {
        yield return ["operation-types", WellKnownMasterDataIds.AdHocOperationType];
        yield return ["services", WellKnownMasterDataIds.AircraftPerLandingService];
        yield return ["services", WellKnownMasterDataIds.OnCallService];
    }

    [Theory]
    [MemberData(nameof(SeededCatalogRoutes))]
    public async Task Seeded_operation_and_service_catalogs_are_protected_from_update_and_deactivate(string route, Guid id)
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var before = await client.GetFromJsonAsync<CatalogDetail>($"{Base}/{route}/{id}");

        before.ShouldNotBeNull();
        before!.IsSystem.ShouldBeTrue();

        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/{route}/{id}")
        {
            Content = JsonContent.Create(new { name = $"{before.Name} Updated", description = before.Description })
        };
        update.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);

        var updateResponse = await client.SendAsync(update);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict, await updateResponse.Content.ReadAsStringAsync());

        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{route}/{id}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);

        var deactivateResponse = await client.SendAsync(deactivate);
        deactivateResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict, await deactivateResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task List_endpoints_bound_extreme_pagination_requests()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();

        var list = await client.GetFromJsonAsync<PagedList<CatalogItem>>(
            $"{Base}/services?page={int.MaxValue}&pageSize={int.MaxValue}");

        list.ShouldNotBeNull();
        list!.PageSize.ShouldBe(100);
        list.Page.ShouldBeGreaterThan(1);
        list.TotalCount.ShouldBeGreaterThan(0);
        list.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("services", "Service")]
    [InlineData("operation-types", "Operation Type")]
    [InlineData("general-supports", "General Support")]
    public async Task Simple_catalog_create_update_and_deactivate_round_trips(string route, string label)
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        await AssertSimpleCatalogRoundTripAsync(client, route, $"{label} {Guid.NewGuid():N}");
    }

    [Fact]
    public async Task Material_create_update_and_deactivate_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var name = $"Material {Guid.NewGuid():N}";

        await AssertSimpleCatalogRoundTripAsync(client, "materials", name);
    }

    [Fact]
    public async Task Aircraft_type_create_update_and_deactivate_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var model = $"m{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync($"{Base}/aircraft-types", new
        {
            manufacturer = "Airbus",
            model,
            notes = "Initial"
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var before = await client.GetFromJsonAsync<AircraftTypeDetail>($"{Base}/aircraft-types/{id}");
        before!.Manufacturer.ShouldBe("Airbus");
        before.Model.ShouldBe(model.ToUpperInvariant());
        before.Notes.ShouldBe("Initial");

        var updatedModel = $"b{Guid.NewGuid():N}";
        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/aircraft-types/{id}")
        {
            Content = JsonContent.Create(new { manufacturer = "Boeing", model = updatedModel, notes = "Updated" })
        };
        update.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        var updateResponse = await client.SendAsync(update);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await updateResponse.Content.ReadAsStringAsync());

        var after = await client.GetFromJsonAsync<AircraftTypeDetail>($"{Base}/aircraft-types/{id}");
        after!.Manufacturer.ShouldBe("Boeing");
        after.Model.ShouldBe(updatedModel.ToUpperInvariant());
        after.Notes.ShouldBe("Updated");
        after.RowVersion.ShouldNotBe(before.RowVersion);

        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/aircraft-types/{id}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", after.RowVersion);
        var deactivateResponse = await client.SendAsync(deactivate);
        deactivateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deactivateResponse.Content.ReadAsStringAsync());

        var list = await client.GetFromJsonAsync<PagedList<AircraftTypeItem>>(
            $"{Base}/aircraft-types?isActive=false&search={Uri.EscapeDataString(updatedModel)}");
        list!.Items.ShouldContain(a => a.Id == id && !a.IsActive);
    }

    [Fact]
    public async Task Tool_create_with_equipment_then_update_reconciles_equipment_rows()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var name = $"Tool {Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync($"{Base}/tools", new
        {
            name,
            description = "Initial",
            equipments = new[]
            {
                new { id = (Guid?)null, factoryId = "F-1", serialId = "S-1", calibrationDate = (DateOnly?)null }
            }
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var before = await client.GetFromJsonAsync<ToolDetail>($"{Base}/tools/{id}");
        before!.Equipments.Count.ShouldBe(1);

        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/tools/{id}")
        {
            Content = JsonContent.Create(new
            {
                name,
                description = "Updated",
                equipments = new[]
                {
                    new { id = (Guid?)before.Equipments[0].Id, factoryId = "F-2", serialId = "S-2", calibrationDate = (DateOnly?)new DateOnly(2026, 6, 1) },
                    new { id = (Guid?)null, factoryId = "F-3", serialId = "S-3", calibrationDate = (DateOnly?)null }
                }
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        var updateResponse = await client.SendAsync(update);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await updateResponse.Content.ReadAsStringAsync());

        var after = await client.GetFromJsonAsync<ToolDetail>($"{Base}/tools/{id}");
        after!.Equipments.Count.ShouldBe(2);
        after.Equipments.ShouldContain(e => e.FactoryId == "F-2" && e.SerialId == "S-2" && e.CalibrationDate == new DateOnly(2026, 6, 1));
        after.Equipments.ShouldContain(e => e.FactoryId == "F-3" && e.SerialId == "S-3");
    }

    private static async Task AssertSimpleCatalogRoundTripAsync(HttpClient client, string route, string name)
    {
        var create = await client.PostAsJsonAsync($"{Base}/{route}", new { name, description = "Initial" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var before = await client.GetFromJsonAsync<CatalogDetail>($"{Base}/{route}/{id}");
        before!.Name.ShouldBe(name);
        before.Description.ShouldBe("Initial");

        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/{route}/{id}")
        {
            Content = JsonContent.Create(new { name = $"{name} Updated", description = "Updated" })
        };
        update.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        var updateResponse = await client.SendAsync(update);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await updateResponse.Content.ReadAsStringAsync());

        var after = await client.GetFromJsonAsync<CatalogDetail>($"{Base}/{route}/{id}");
        after!.Name.ShouldBe($"{name} Updated");
        after.Description.ShouldBe("Updated");
        after.RowVersion.ShouldNotBe(before.RowVersion);

        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{route}/{id}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", after.RowVersion);
        var deactivateResponse = await client.SendAsync(deactivate);
        deactivateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deactivateResponse.Content.ReadAsStringAsync());

        var list = await client.GetFromJsonAsync<PagedList<CatalogItem>>(
            $"{Base}/{route}?isActive=false&search={Uri.EscapeDataString(name)}");
        list!.Items.ShouldContain(m => m.Id == id && !m.IsActive);
    }
}
