using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Events;
using Core.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Currency;

public sealed class Currency : AggregateRoot<CurrencyId>
{
    private readonly List<ExchangeRate> _exchangeRates = [];

    public CurrencyCode Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public IReadOnlyList<ExchangeRate> ExchangeRates => _exchangeRates.AsReadOnly();

    private Currency() { }

    public static Result<Currency> Create(
        CurrencyCode code,
        string name,
        Guid currentUserId,
        IReadOnlyList<(CurrencyId ToCurrencyId, decimal Rate)>? initialExchangeRates = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Currency name is required.");

        if (name.Length > 100)
            return Error.Validation("Currency name must not exceed 100 characters.");

        if (currentUserId == Guid.Empty)
            return Error.Validation("Current user is required for exchange rate lines.");

        var currency = new Currency
        {
            Id = CurrencyId.New(),
            Code = code,
            Name = name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        currency.RaiseDomainEvent(new CurrencyCreatedEvent(currency.Id));

        if (initialExchangeRates is not null && initialExchangeRates.Count > 0)
        {
            var duplicateTo = initialExchangeRates.GroupBy(x => x.ToCurrencyId).Where(g => g.Count() > 1).ToList();
            if (duplicateTo.Count > 0)
                return Error.Validation("Duplicate target currency in exchange rate list.");

            foreach (var row in initialExchangeRates)
            {
                var add = currency.AddExchangeRate(row.ToCurrencyId, row.Rate, currentUserId);
                if (add.IsFailure) return add.Error;
            }
        }

        return currency;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Currency is already active.");

        IsActive = true;
        RaiseDomainEvent(new CurrencyActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Currency is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new CurrencyDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result UpdateDetails(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Currency name is required.");

        if (name.Length > 100)
            return Error.Validation("Currency name must not exceed 100 characters.");

        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Applies a full outbound-rate snapshot from the caller: explicit ids update rows; null ids insert;
    /// any persisted row whose id does not appear in the snapshot as <paramref name="ExistingRateId"/> is removed.
    /// </summary>
    public Result SyncExchangeRates(
        IReadOnlyList<(Guid? ExistingRateId, CurrencyId ToCurrencyId, decimal Rate)> incoming,
        Guid currentUserId)
    {
        if (currentUserId == Guid.Empty)
            return Error.Validation("Current user is required.");

        var rows = incoming?.ToList() ?? [];
        if (rows.GroupBy(x => x.ToCurrencyId).Any(g => g.Count() > 1))
            return Error.Validation("Duplicate target currency in exchange rate list.");

        foreach (var row in rows.Where(x => x.ExistingRateId is not null))
        {
            var rid = ExchangeRateId.From(row.ExistingRateId!.Value);
            var rate = _exchangeRates.FirstOrDefault(r => r.Id == rid);
            if (rate is null)
                return Error.NotFound("Exchange rate not found.");

            if (rate.ToCurrencyId != row.ToCurrencyId)
                return Error.Validation("Cannot change the target currency of an existing exchange rate row.");

            var upd = rate.UpdateRate(row.Rate);
            if (upd.IsFailure) return upd.Error;
        }

        foreach (var rate in _exchangeRates.ToList())
        {
            var keep = rows.Any(r => r.ExistingRateId == rate.Id.Value);
            if (!keep)
            {
                var rem = RemoveExchangeRate(rate.Id);
                if (rem.IsFailure) return rem.Error;
            }
        }

        foreach (var row in rows.Where(x => x.ExistingRateId is null))
        {
            var add = AddExchangeRate(row.ToCurrencyId, row.Rate, currentUserId);
            if (add.IsFailure) return add.Error;
        }

        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public static Currency CreateSeed(Guid id, CurrencyCode code, string name)
    {
        return new Currency
        {
            Id = CurrencyId.From(id),
            Code = code,
            Name = name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public Result<ExchangeRate> AddExchangeRate(CurrencyId toCurrencyId, decimal rate, Guid createdById)
    {
        if (toCurrencyId == Id)
            return Error.Validation("Cannot set an exchange rate to the same currency.");

        if (_exchangeRates.Any(r => r.ToCurrencyId == toCurrencyId))
            return Error.Validation("An exchange rate for this target currency already exists.");

        var result = ExchangeRate.Create(Id, toCurrencyId, rate, createdById);
        if (result.IsFailure) return result.Error;

        var exchangeRate = result.Value;
        _exchangeRates.Add(exchangeRate);

        RaiseDomainEvent(new ExchangeRateSetEvent(
            exchangeRate.Id,
            Id,
            toCurrencyId,
            rate,
            createdById));

        return exchangeRate;
    }

    public Result RemoveExchangeRate(ExchangeRateId exchangeRateId)
    {
        var rate = _exchangeRates.FirstOrDefault(r => r.Id == exchangeRateId);
        if (rate is null)
            return Error.NotFound("Exchange rate not found.");

        _exchangeRates.Remove(rate);
        RaiseDomainEvent(new ExchangeRateRemovedEvent(Id, exchangeRateId));
        return Result.Success();
    }
}
