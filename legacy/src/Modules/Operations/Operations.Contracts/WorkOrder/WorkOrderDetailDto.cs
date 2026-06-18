using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Operations.Contracts.Flight;
using Operations.Domain.Enumerations;

namespace Operations.Contracts.WorkOrder;

/// <summary>
/// Full work order projection used by the review dialog: header data plus the three
/// line collections that make up the billable work performed. <see cref="CustomerSignature"/>
/// holds the PNG bytes captured by the mobile signature pad — System.Text.Json serialises
/// it as a Base64 string the portal can drop straight into a data URL.
/// </summary>
public sealed record WorkOrderDetailDto(
    Guid Id,
    string? WorkOrderNo,
    FlightSnapshot? FlightSnapshot,
    CustomerSnapshot CustomerSnapshot,
    StationSnapshot StationSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    AircraftTypeSnapshot? AircraftTypeSnapshot,
    string? AircraftTailNumber,
    string FlightNumber,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    DateTimeOffset? Ata,
    DateTimeOffset? Atd,
    bool IsCanceled,
    DateTimeOffset? CanceledAt,
    WorkOrderStatus Status,
    DateTimeOffset? MarkedForDeletionAt,
    string? Remarks,
    Guid? CreatedByEmployeeId,
    IReadOnlyList<WorkOrderServiceLineDto> ServiceLines,
    IReadOnlyList<WorkOrderTaskDto> Tasks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    byte[]? CustomerSignature = null);
