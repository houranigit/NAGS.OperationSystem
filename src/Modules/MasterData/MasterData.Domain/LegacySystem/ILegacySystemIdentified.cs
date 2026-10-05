using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.LegacySystem;

/// <summary>Optional mapping used to transfer this master record into a legacy system.</summary>
public interface ILegacySystemIdentified
{
    public string? LegacySystemId { get; }
    public Result SetLegacySystemId(string? value, DateTimeOffset now);
}

public static class LegacySystemIdRules
{
    public const int MaximumLength = 200;

    public static Result<string?> Normalize(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > MaximumLength)
            return Error.Validation(
                "Legacy system ID must be at most 200 characters.",
                "MasterData.LegacySystemId.TooLong");

        return Result.Success(normalized);
    }
}
