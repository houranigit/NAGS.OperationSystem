using BuildingBlocks.Domain.Results;

namespace Operations.Domain.ValueObjects;

public static class WorkOrderNumber
{
    public static Result<string> Format(string stationIataCode, int sequence)
    {
        if (string.IsNullOrWhiteSpace(stationIataCode) || stationIataCode.Trim().Length != 3)
            return Error.Validation("Station IATA code must be three characters.", "Operations.WorkOrderNumber.StationInvalid");
        if (sequence <= 0)
            return Error.Validation("Approval sequence must be greater than zero.", "Operations.WorkOrderNumber.SequenceInvalid");

        return $"{stationIataCode.Trim().ToUpperInvariant()}-{sequence:D4}";
    }
}
