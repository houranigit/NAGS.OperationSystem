using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Events;

namespace Core.Domain.Aggregates.Service;

public sealed class Service : AggregateRoot<ServiceId>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Service() { }

    public static Result<Service> Create(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Service name is required.");

        if (name.Length > 100)
            return Error.Validation("Service name must not exceed 100 characters.");

        if (description?.Length > 500)
            return Error.Validation("Description must not exceed 500 characters.");

        var service = new Service
        {
            Id = ServiceId.New(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        service.RaiseDomainEvent(new ServiceCreatedEvent(service.Id));
        return service;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Service is already active.");

        IsActive = true;
        RaiseDomainEvent(new ServiceActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Service is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new ServiceDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result UpdateDetails(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Service name is required.");

        if (name.Length > 100)
            return Error.Validation("Service name must not exceed 100 characters.");

        if (description?.Length > 500)
            return Error.Validation("Description must not exceed 500 characters.");

        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Creates a service with a predetermined ID for deterministic data seeding (no domain events).</summary>
    public static Service CreateSeed(Guid id, string name, string? description = null)
    {
        return new Service
        {
            Id = ServiceId.From(id),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }
}
