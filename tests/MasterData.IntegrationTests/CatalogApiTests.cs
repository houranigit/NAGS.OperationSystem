using System.Net;
using System.Net.Http.Json;
using MasterData.Contracts.Seeding;
using Shouldly;

namespace MasterData.IntegrationTests;

public class CatalogApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record CatalogDetail(Guid Id, string Name, string? Description, bool IsActive,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);
    private sealed record CatalogItem(Guid Id, string Name, string? Description, bool IsActive);
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
        aircraftPerLanding!.Name.ShouldBe("Aircraft Per Landing");
        onCall!.Name.ShouldBe("On Call");
    }

    [Fact]
    public async Task Material_create_update_and_deactivate_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var name = $"Material {Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync($"{Base}/materials", new { name, description = "Initial" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var before = await client.GetFromJsonAsync<CatalogDetail>($"{Base}/materials/{id}");
        before!.Name.ShouldBe(name);
        before.Description.ShouldBe("Initial");

        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/materials/{id}")
        {
            Content = JsonContent.Create(new { name = $"{name} Updated", description = "Updated" })
        };
        update.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<CatalogDetail>($"{Base}/materials/{id}");
        after!.Name.ShouldBe($"{name} Updated");
        after.RowVersion.ShouldNotBe(before.RowVersion);

        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/materials/{id}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", after.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<PagedList<CatalogItem>>($"{Base}/materials?isActive=false&search={Uri.EscapeDataString(name)}");
        list!.Items.ShouldContain(m => m.Id == id && !m.IsActive);
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
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<ToolDetail>($"{Base}/tools/{id}");
        after!.Equipments.Count.ShouldBe(2);
        after.Equipments.ShouldContain(e => e.FactoryId == "F-2" && e.SerialId == "S-2" && e.CalibrationDate == new DateOnly(2026, 6, 1));
        after.Equipments.ShouldContain(e => e.FactoryId == "F-3" && e.SerialId == "S-3");
    }
}
