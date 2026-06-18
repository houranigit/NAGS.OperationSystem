using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.Service;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;

namespace Operations.Contracts.Mobile;

/// <summary>
/// Aggregated lookup payload for the mobile app. The Android client fetches this once
/// at sign-in (and on user-triggered refresh) and caches every list locally so every
/// work-order picker (scheduled-flight or ad-hoc) works fully offline.
/// </summary>
/// <param name="AircraftTypes">All active aircraft types — used by the work-order header.</param>
/// <param name="Services">
/// Active non-AOG services only — work orders cannot bill AOG; the AOG catalog entry
/// is omitted so mobile pickers stay aligned with the portal.
/// </param>
/// <param name="Tools">All active store tools — feed the task editor's tool multi-select.</param>
/// <param name="Materials">All active store materials.</param>
/// <param name="GeneralSupports">All active general-support items.</param>
/// <param name="Customers">
/// All active airline customers — needed by the "Create work order from scratch"
/// flow which has to attach the ad-hoc flight to a real customer.
/// </param>
/// <param name="StationEmployees">
/// Active employees at the caller's station. Used by the work-order service-line and
/// task employee pickers so the user can record work performed by anyone at the
/// station (not just flight assignees).
/// </param>
/// <param name="GeneratedAt">Server clock when this payload was assembled.</param>
public sealed record MobileLookupsDto(
    IReadOnlyList<AircraftTypeSnapshot> AircraftTypes,
    IReadOnlyList<ServiceSnapshot> Services,
    IReadOnlyList<ToolSnapshot> Tools,
    IReadOnlyList<MaterialSnapshot> Materials,
    IReadOnlyList<GeneralSupportSnapshot> GeneralSupports,
    IReadOnlyList<CustomerSnapshot> Customers,
    IReadOnlyList<EmployeeSnapshot> StationEmployees,
    DateTimeOffset GeneratedAt);
