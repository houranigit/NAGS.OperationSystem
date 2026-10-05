using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Authorization;
using MasterData.Application.Features.Customers;
using MasterData.Domain.Countries;
using MasterData.Application.Features.Services;
using MasterData.Application.Features.Tools;
using MasterData.Application.Features.Materials;
using MasterData.Application.Features.GeneralSupports;
using MasterData.Application.Features.OperationTypes;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Resources;
using MasterData.Contracts.Seeding;
using MasterData.Domain.Customers;
using MasterData.Domain.GeneralSupports;
using MasterData.Domain.LegacySystem;
using MasterData.Domain.ManpowerTypes;
using MasterData.Domain.OperationTypes;
using MasterData.Domain.Materials;
using MasterData.Domain.Services;
using MasterData.Domain.StaffMembers;
using MasterData.Domain.Tools;
using MasterData.Infrastructure.Persistence;
using MasterData.Infrastructure.Readers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MasterData.Application.UnitTests;

public sealed class LegacySystemIdTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_update_and_clear_are_persisted_and_omitted_update_preserves_mapping()
    {
        await using var db = CreateDb();
        var create = await new CreateServiceCommandHandler(db, TimeProvider.System).Handle(
            new CreateServiceCommand("A Check", null, "  00123  "), CancellationToken.None);
        create.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();

        var detail = await new GetServiceByIdQueryHandler(db).Handle(
            new GetServiceByIdQuery(create.Value), CancellationToken.None);
        detail.Value.LegacySystemId.ShouldBe("00123");
        var handler = new UpdateServiceCommandHandler(db, TimeProvider.System);
        var update = new UpdateServiceCommand(create.Value, "A Check Updated", null, []);
        (await handler.Handle(update, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        (await db.Services.SingleAsync()).LegacySystemId.ShouldBe("00123");

        (await handler.Handle(update with { LegacySystemId = " XYZ123 " }, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        (await db.Services.SingleAsync()).LegacySystemId.ShouldBe("XYZ123");

        (await handler.Handle(update with { LegacySystemId = string.Empty }, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        (await db.Services.SingleAsync()).LegacySystemId.ShouldBeNull();
    }

    [Fact]
    public async Task Aircraft_per_landing_can_be_mapped_but_system_service_fields_stay_protected()
    {
        await using var db = CreateDb();
        var service = Service.Create("Aircraft Per Landing", null, Now,
            WellKnownMasterDataIds.AircraftPerLandingService).Value;
        db.Services.Add(service);
        await db.SaveChangesAsync();
        var handler = new UpdateServiceCommandHandler(db, TimeProvider.System);
        var mapping = new UpdateServiceCommand(service.Id, service.Name, service.Description, [], "PL001");

        (await handler.Handle(mapping, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        (await db.Services.SingleAsync()).LegacySystemId.ShouldBe("PL001");

        var rename = await handler.Handle(mapping with { Name = "Changed" }, CancellationToken.None);
        rename.IsFailure.ShouldBeTrue();
        rename.Error.Code.ShouldBe("MasterData.Service.SystemProtected");
        (await db.Services.SingleAsync()).Name.ShouldBe("Aircraft Per Landing");
        (await handler.Handle(mapping with { LegacySystemId = null }, CancellationToken.None))
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Protected_customer_mapping_can_change_without_unlocking_customer_fields()
    {
        await using var db = CreateDb();
        var country = Country.Create("Saudi Arabia", "SA", Now).Value;
        var customer = Customer.Create(null, null, "Unknown", country.Id, null, null, null,
            Address.Create(null, null, null, null, null).Value, Now, WellKnownMasterDataIds.UnknownCustomer).Value;
        db.AddRange(country, customer);
        await db.SaveChangesAsync();
        var handler = new UpdateCustomerCommandHandler(db, new AdministratorScope(), TimeProvider.System);
        var mapping = new UpdateCustomerCommand(customer.Id, null, null, customer.Name, country.Id,
            null, null, new CustomerAddressInput(null, null, null, null, null), [], "UNKNOWN-CUSTOMER");

        (await handler.Handle(mapping, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        customer.LegacySystemId.ShouldBe("UNKNOWN-CUSTOMER");
        var result = await handler.Handle(mapping with
        {
            Address = mapping.Address with { City = "Changed" }
        }, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Customer.SystemProtected");
        customer.Address.City.ShouldBeNull();
    }

    [Fact]
    public async Task Protected_catalogs_allow_mapping_changes_and_reject_normal_field_changes()
    {
        await using var db = CreateDb();
        var tool = Tool.Create("Unknown", null, Now, WellKnownMasterDataIds.UnknownTool).Value;
        var material = Material.Create("Unknown", null, Now, WellKnownMasterDataIds.UnknownMaterial).Value;
        var support = GeneralSupport.Create("Unknown", null, Now, WellKnownMasterDataIds.UnknownGeneralSupport).Value;
        var operation = OperationType.Create("Adhoc", null, Now, WellKnownMasterDataIds.AdHocOperationType).Value;
        db.AddRange(tool, material, support, operation);
        await db.SaveChangesAsync();
        var toolHandler = new UpdateToolCommandHandler(db, TimeProvider.System);
        var materialHandler = new UpdateMaterialCommandHandler(db, TimeProvider.System);
        var supportHandler = new UpdateGeneralSupportCommandHandler(db, TimeProvider.System);
        var operationHandler = new UpdateOperationTypeCommandHandler(db, TimeProvider.System);
        var toolMapping = new UpdateToolCommand(tool.Id, tool.Name, null, [], [], LegacySystemId: "TOOL");
        var materialMapping = new UpdateMaterialCommand(material.Id, material.Name, null, [], LegacySystemId: "MAT");
        var supportMapping = new UpdateGeneralSupportCommand(support.Id, support.Name, null, [], LegacySystemId: "SUP");
        var operationMapping = new UpdateOperationTypeCommand(operation.Id, operation.Name, null, [], "OP");

        (await toolHandler.Handle(toolMapping, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await materialHandler.Handle(materialMapping, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await supportHandler.Handle(supportMapping, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await operationHandler.Handle(operationMapping, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        tool.LegacySystemId.ShouldBe("TOOL");
        material.LegacySystemId.ShouldBe("MAT");
        support.LegacySystemId.ShouldBe("SUP");
        operation.LegacySystemId.ShouldBe("OP");

        (await toolHandler.Handle(toolMapping with { CalculationType = ResourceCalculationType.Quantity },
            CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await materialHandler.Handle(materialMapping with { Name = "Changed" }, CancellationToken.None))
            .IsFailure.ShouldBeTrue();
        (await supportHandler.Handle(supportMapping with { Description = "Changed" }, CancellationToken.None))
            .IsFailure.ShouldBeTrue();
        (await operationHandler.Handle(operationMapping with { Name = "Changed" }, CancellationToken.None))
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Export_lookup_is_type_separated_includes_inactive_records_and_resolves_staff_manpower()
    {
        await using var db = CreateDb();
        var sharedId = Guid.NewGuid();
        var service = Service.Create("Shared name", null, Now, sharedId).Value;
        var tool = Tool.Create("Shared name", null, Now, sharedId).Value;
        var material = Material.Create("Shared name", null, Now, sharedId).Value;
        var support = GeneralSupport.Create("Shared name", null, Now, sharedId).Value;
        var manpower = ManpowerType.Create("Engineer", null, Now).Value;
        var staff = StaffMember.Create("Staff", "EMP1", "staff@example.com", Guid.NewGuid(),
            manpower.Id, null, null, Now).Value;
        service.SetLegacySystemId("SERVICE", Now);
        service.Deactivate(Now);
        tool.SetLegacySystemId("TOOL", Now);
        material.SetLegacySystemId("MATERIAL", Now);
        support.SetLegacySystemId("SUPPORT", Now);
        staff.SetLegacySystemId("STAFF", Now);
        manpower.SetLegacySystemId("MANPOWER", Now);
        db.AddRange(service, tool, material, support, staff, manpower);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var lookup = await new LegacySystemIdReader(db).GetAsync(new LegacySystemIdRequest(
            Services: [sharedId, sharedId], Tools: [sharedId], Materials: [sharedId],
            GeneralSupports: [sharedId], StaffMembers: [staff.Id]), CancellationToken.None);

        lookup.Services.ShouldHaveSingleItem();
        lookup.Services[sharedId].ShouldBe("SERVICE");
        lookup.Tools[sharedId].ShouldBe("TOOL");
        lookup.Materials[sharedId].ShouldBe("MATERIAL");
        lookup.GeneralSupports[sharedId].ShouldBe("SUPPORT");
        lookup.StaffMembers[staff.Id].ShouldBe("STAFF");
        lookup.StaffMemberManpowerTypeIds[staff.Id].ShouldBe(manpower.Id);
        lookup.ManpowerTypes[manpower.Id].ShouldBe("MANPOWER");
        lookup.Customers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unmapped_and_missing_records_do_not_fall_back_to_current_names()
    {
        await using var db = CreateDb();
        var service = Service.Create("Human-readable name", null, Now).Value;
        db.Services.Add(service);
        await db.SaveChangesAsync();
        var missing = Guid.NewGuid();

        var lookup = await new LegacySystemIdReader(db).GetAsync(
            new LegacySystemIdRequest(Services: [service.Id, missing]), CancellationToken.None);

        lookup.Services[service.Id].ShouldBeNull();
        lookup.Services.ContainsKey(missing).ShouldBeFalse();
    }

    [Fact]
    public void Sql_model_limits_mapping_to_requested_ten_entities_and_does_not_require_uniqueness()
    {
        using var db = new MasterDataDbContext(new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseSqlServer("Server=localhost;Database=SchemaOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        var mappedEntities = db.Model.GetEntityTypes()
            .Where(x => x.FindProperty(nameof(ILegacySystemIdentified.LegacySystemId)) is not null)
            .ToList();

        mappedEntities.Count.ShouldBe(10);
        foreach (var entity in mappedEntities)
        {
            typeof(ILegacySystemIdentified).IsAssignableFrom(entity.ClrType).ShouldBeTrue();
            var property = entity.FindProperty(nameof(ILegacySystemIdentified.LegacySystemId))!;
            property.IsNullable.ShouldBeTrue();
            property.GetMaxLength().ShouldBe(200);
            entity.GetIndexes().Any(index => index.IsUnique && index.Properties.Contains(property))
                .ShouldBeFalse();
        }
    }

    [Fact]
    public void Validator_enforces_trimmed_length_and_accepts_blank_or_omitted_mapping()
    {
        var validator = new CreateServiceCommandValidator();
        validator.Validate(new CreateServiceCommand("A Check", null)).IsValid.ShouldBeTrue();
        validator.Validate(new CreateServiceCommand("A Check", null, " ")).IsValid.ShouldBeTrue();
        validator.Validate(new CreateServiceCommand("A Check", null, "  " + new string('0', 200) + "  "))
            .IsValid.ShouldBeTrue();
        validator.Validate(new CreateServiceCommand("A Check", null, new string('0', 201)))
            .IsValid.ShouldBeFalse();
    }

    private sealed class AdministratorScope : IMasterDataScope
    {
        public Task<Result<MasterDataScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new MasterDataScopeContext(UserType.SystemAdministrator, null, null)));
    }

    private static MasterDataDbContext CreateDb() => new(new DbContextOptionsBuilder<MasterDataDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
