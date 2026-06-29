using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.AircraftTypes;

/// <summary>Aircraft type catalog item identified by manufacturer and model.</summary>
public sealed class AircraftType : AggregateRoot<Guid>
{
    private AircraftType() { }

    public AircraftManufacturer Manufacturer { get; private set; }
    public string Model { get; private set; } = null!;
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<AircraftType> Create(
        AircraftManufacturer manufacturer,
        string? model,
        string? notes,
        DateTimeOffset now,
        Guid? id = null)
    {
        var modelCheck = ValidateModel(model);
        if (modelCheck.IsFailure)
            return modelCheck.Error;

        var notesCheck = ValidateNotes(notes);
        if (notesCheck.IsFailure)
            return notesCheck.Error;

        return new AircraftType
        {
            Id = id ?? Guid.NewGuid(),
            Manufacturer = manufacturer,
            Model = modelCheck.Value,
            Notes = notesCheck.Value,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public Result Update(AircraftManufacturer manufacturer, string? model, string? notes, DateTimeOffset now)
    {
        var modelCheck = ValidateModel(model);
        if (modelCheck.IsFailure)
            return modelCheck.Error;

        var notesCheck = ValidateNotes(notes);
        if (notesCheck.IsFailure)
            return notesCheck.Error;

        Manufacturer = manufacturer;
        Model = modelCheck.Value;
        Notes = notesCheck.Value;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Activate(DateTimeOffset now)
    {
        if (IsActive)
            return Result.Success();

        IsActive = true;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return Result.Success();

        IsActive = false;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    private static Result<string> ValidateModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return Error.Validation("Aircraft model is required.", "MasterData.AircraftType.ModelRequired");

        var normalized = model.Trim().ToUpperInvariant();
        if (normalized.Length > 50)
            return Error.Validation("Aircraft model must be at most 50 characters.", "MasterData.AircraftType.ModelTooLong");

        return normalized;
    }

    private static Result<string?> ValidateNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return Result.Success<string?>(null);

        var trimmed = notes.Trim();
        if (trimmed.Length > 500)
            return Error.Validation("Notes must be at most 500 characters.", "MasterData.AircraftType.NotesTooLong");

        return Result.Success<string?>(trimmed);
    }
}
