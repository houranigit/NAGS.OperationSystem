using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Store.Domain.Events;

namespace Store.Domain.Aggregates.Unit;

/// <summary>
/// User-managed unit of measure shared by <see cref="Material.Material"/> and
/// <see cref="GeneralSupport.GeneralSupport"/>. Examples: ROLL, GALLON, KG, TIN, CAN, METER, PIECE, EVENT.
/// </summary>
public sealed class Unit : AggregateRoot<UnitId>
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Unit() { }

    public static Result<Unit> Create(string code, string name)
    {
        var codeResult = NormalizeCode(code);
        if (codeResult.IsFailure) return codeResult.Error;

        var nameResult = ValidateName(name);
        if (nameResult is not null) return nameResult;

        var unit = new Unit
        {
            Id = UnitId.New(),
            Code = codeResult.Value,
            Name = name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        unit.RaiseDomainEvent(new UnitCreatedEvent(unit.Id));
        return unit;
    }

    public Result UpdateDetails(string code, string name)
    {
        var codeResult = NormalizeCode(code);
        if (codeResult.IsFailure) return codeResult.Error;

        var nameResult = ValidateName(name);
        if (nameResult is not null) return nameResult;

        Code = codeResult.Value;
        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Unit is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new UnitActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Unit is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new UnitDeactivatedEvent(Id));
        return Result.Success();
    }

    private static Result<string> NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation("Unit code is required.");

        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length > 20)
            return Error.Validation("Unit code must not exceed 20 characters.");

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '/' || ch == '.'))
                return Error.Validation("Unit code may only contain letters, digits, '_', '-', '/' or '.'.");
        }

        return trimmed;
    }

    private static Error? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Unit name is required.");
        if (name.Length > 100)
            return Error.Validation("Unit name must not exceed 100 characters.");
        return null;
    }
}
