using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Service;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;

namespace Operations.Contracts.Mobile;

/// <summary>
/// Lean catalog bundle for the v2 mobile client. Carries only the lookups that
/// the new Android app caches locally as separate Room tables: services, tools,
/// materials, general supports, customers, and aircraft types. Station employees
/// are fetched from <c>/api/mobile/v2/employees/at-my-station</c>.
/// </summary>
public sealed record MobileV2CatalogsDto(
    IReadOnlyList<ServiceSnapshot> Services,
    IReadOnlyList<ToolSnapshot> Tools,
    IReadOnlyList<MaterialSnapshot> Materials,
    IReadOnlyList<GeneralSupportSnapshot> GeneralSupports,
    IReadOnlyList<CustomerSnapshot> Customers,
    IReadOnlyList<AircraftTypeSnapshot> AircraftTypes,
    DateTimeOffset GeneratedAt);
