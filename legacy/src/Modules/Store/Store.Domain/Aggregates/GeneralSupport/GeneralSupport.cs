using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Unit;
using Store.Domain.Events;

namespace Store.Domain.Aggregates.GeneralSupport;

/// <summary>
/// A miscellaneous billable support item — anything from "Storage Space" to "Iqama Renewal" to
/// "Customs Clearance" — measured in a user-managed <see cref="Unit"/> and flagged as duration-
/// based or single-event via <see cref="IsDuration"/>.
/// </summary>
public sealed class GeneralSupport : AggregateRoot<GeneralSupportId>
{
    public string Name { get; private set; } = null!;
    public UnitId UnitId { get; private set; } = null!;

    /// <summary>
    /// True when the item is billed over a duration (e.g. monthly rentals, accommodation),
    /// false when it is a one-shot/event item (e.g. landing fee, customs clearance).
    /// </summary>
    public bool IsDuration { get; private set; }

    public string? Note { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private GeneralSupport() { }

    public static Result<GeneralSupport> Create(string name, UnitId unitId, bool isDuration, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(unitId);

        var nameError = ValidateName(name);
        if (nameError is not null) return nameError;

        var noteError = ValidateNote(note);
        if (noteError is not null) return noteError;

        var item = new GeneralSupport
        {
            Id = GeneralSupportId.New(),
            Name = name.Trim(),
            UnitId = unitId,
            IsDuration = isDuration,
            Note = note?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        item.RaiseDomainEvent(new GeneralSupportCreatedEvent(item.Id));
        return item;
    }

    public Result UpdateDetails(string name, UnitId unitId, bool isDuration, string? note)
    {
        ArgumentNullException.ThrowIfNull(unitId);

        var nameError = ValidateName(name);
        if (nameError is not null) return nameError;

        var noteError = ValidateNote(note);
        if (noteError is not null) return noteError;

        Name = name.Trim();
        UnitId = unitId;
        IsDuration = isDuration;
        Note = note?.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("General support is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new GeneralSupportActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("General support is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new GeneralSupportDeactivatedEvent(Id));
        return Result.Success();
    }

    private static Error? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("General support name is required.");
        if (name.Length > 200)
            return Error.Validation("General support name must not exceed 200 characters.");
        return null;
    }

    private static Error? ValidateNote(string? note)
    {
        if (note is { Length: > 500 })
            return Error.Validation("Note must not exceed 500 characters.");
        return null;
    }
}
