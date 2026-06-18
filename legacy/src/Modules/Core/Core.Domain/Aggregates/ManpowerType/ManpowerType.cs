using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Events;

namespace Core.Domain.Aggregates.ManpowerType;

public sealed class ManpowerType : AggregateRoot<ManpowerTypeId>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private ManpowerType() { }

    public static Result<ManpowerType> Create(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Manpower type name is required.");

        if (name.Length > 100)
            return Error.Validation("Manpower type name must not exceed 100 characters.");

        if (description?.Length > 500)
            return Error.Validation("Description must not exceed 500 characters.");

        var manpowerType = new ManpowerType
        {
            Id = ManpowerTypeId.New(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        manpowerType.RaiseDomainEvent(new ManpowerTypeCreatedEvent(manpowerType.Id));
        return manpowerType;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Manpower type is already active.");

        IsActive = true;
        RaiseDomainEvent(new ManpowerTypeActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Manpower type is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new ManpowerTypeDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result UpdateDetails(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Manpower type name is required.");

        if (name.Length > 100)
            return Error.Validation("Manpower type name must not exceed 100 characters.");

        if (description?.Length > 500)
            return Error.Validation("Description must not exceed 500 characters.");

        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
