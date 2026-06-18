using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Enumerations;
using Core.Domain.Events;

namespace Core.Domain.Aggregates.AircraftType;

/// <summary>Aircraft type master data (model-centric; no ICAO type designator).</summary>
public sealed class AircraftType : AggregateRoot<AircraftTypeId>
{
    public Manufacturer Manufacturer { get; private set; }
    public string Model { get; private set; } = null!;
    /// <summary>Operational notes or common label (max 500).</summary>
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private AircraftType() { }

    public static Result<AircraftType> Create(Manufacturer manufacturer, string model, string? notes = null)
    {
        var modelErr = ValidateModel(model);
        if (modelErr is not null) return modelErr;

        var notesErr = ValidateNotes(notes);
        if (notesErr is not null) return notesErr;

        var aircraftType = new AircraftType
        {
            Id = AircraftTypeId.New(),
            Manufacturer = manufacturer,
            Model = NormalizeModel(model),
            Notes = NormalizeNotes(notes),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        aircraftType.RaiseDomainEvent(new AircraftTypeCreatedEvent(aircraftType.Id));
        return aircraftType;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Aircraft type is already active.");

        IsActive = true;
        RaiseDomainEvent(new AircraftTypeActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Aircraft type is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new AircraftTypeDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result UpdateDetails(Manufacturer manufacturer, string model, string? notes)
    {
        var modelErr = ValidateModel(model);
        if (modelErr is not null) return modelErr;

        var notesErr = ValidateNotes(notes);
        if (notesErr is not null) return notesErr;

        Manufacturer = manufacturer;
        Model = NormalizeModel(model);
        Notes = NormalizeNotes(notes);
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    private static string NormalizeModel(string model) => model.Trim().ToUpperInvariant();

    private static string? NormalizeNotes(string? notes)
    {
        if (notes is null) return null;
        var t = notes.Trim();
        return t.Length == 0 ? null : t;
    }

    private static Error? ValidateModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return Error.Validation("Model is required.");

        if (NormalizeModel(model).Length > 50)
            return Error.Validation("Model must not exceed 50 characters.");

        return null;
    }

    private static Error? ValidateNotes(string? notes)
    {
        var normalized = NormalizeNotes(notes);
        if (normalized is null) return null;
        if (normalized.Length > 500)
            return Error.Validation("Notes must not exceed 500 characters.");

        return null;
    }
}

