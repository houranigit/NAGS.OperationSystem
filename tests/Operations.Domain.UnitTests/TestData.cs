using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    public static CustomerSnapshot Customer() => new(Guid.NewGuid(), "SV", "Saudia");
    public static StationSnapshot Station() => new(Guid.NewGuid(), "RUH", "Riyadh");
    public static OperationTypeSnapshot OperationType() => new(Guid.NewGuid(), "Transit");
    public static StaffMemberSnapshot Staff() => new(Guid.NewGuid(), "Ahmed Ali", "E1001");
    public static AircraftTypeSnapshot AircraftType() => new(Guid.NewGuid(), "Airbus", "A320");
    public static ServiceSnapshot Service() => new(Guid.NewGuid(), "Marshalling");
    public static ToolSnapshot Tool() => new(Guid.NewGuid(), "Headset");
    public static MaterialSnapshot Material() => new(Guid.NewGuid(), "Sealant");
    public static GeneralSupportSnapshot GeneralSupport() => new(Guid.NewGuid(), "GPU");
    public static ServiceSnapshot PerLandingService() => new(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing");

    public static FlightNumber FlightNo(string value = "SV1020") => FlightNumber.Create(value).Value;

    public static ScheduledTime Schedule() =>
        ScheduledTime.Create(Now, Now.AddHours(1)).Value;
}
