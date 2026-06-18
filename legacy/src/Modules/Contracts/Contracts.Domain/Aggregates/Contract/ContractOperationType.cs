using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Child entity wrapping a frozen <see cref="OperationTypeSnapshot"/> that the contract
/// applies to. Owned by <see cref="Contract"/>. Each entry carries the <em>contract</em>
/// services applicable to flights billed under this OT — those services drive the per-flight
/// service list and the assignment-required vs AOG-only rule. Pricing is configured
/// independently in <see cref="Pricing.ContractService"/>.
/// </summary>
public sealed class ContractOperationType : Entity<ContractOperationTypeId>
{
    public ContractId ContractId { get; private set; } = null!;
    public OperationTypeSnapshot OperationType { get; private set; } = null!;

    private readonly List<ServiceSnapshot> _services = [];

    /// <summary>
    /// Services declared as billable for flights under this OT. The aggregate enforces
    /// "AOG-only OR no-AOG" (no mix) and "≥ 1 service" via <see cref="ValidateServices"/>.
    /// </summary>
    public IReadOnlyList<ServiceSnapshot> Services => _services;

    private ContractOperationType() { }

    /// <summary>
    /// Validates the services list and constructs the entity. Returns a domain
    /// <see cref="Error"/> if the list is empty or mixes AOG with other services.
    /// </summary>
    internal static Result<ContractOperationType> Create(
        ContractId contractId,
        OperationTypeSnapshot operationType,
        IReadOnlyList<ServiceSnapshot> services)
    {
        var check = ValidateServices(operationType, services);
        if (check.IsFailure) return check.Error;

        var entity = new ContractOperationType
        {
            Id = ContractOperationTypeId.New(),
            ContractId = contractId,
            OperationType = operationType
        };
        foreach (var s in services)
            entity._services.Add(s);
        return entity;
    }

    /// <summary>
    /// True when the OT carries only the AOG service. Used by <c>Flight</c> to derive
    /// "assignment optional" — non-AOG OTs require at least one assigned employee.
    /// </summary>
    public bool IsAogOnly() => _services.Count == 1 && _services[0].IsAog;

    private static Result ValidateServices(
        OperationTypeSnapshot operationType,
        IReadOnlyList<ServiceSnapshot> services)
    {
        if (services is null || services.Count == 0)
            return Error.Validation(
                $"Operation type '{operationType.Name}' must have at least 1 service.");

        var seen = new HashSet<Guid>();
        var anyAog = false;
        var anyNonAog = false;

        foreach (var s in services)
        {
            if (s is null)
                return Error.Validation(
                    $"Operation type '{operationType.Name}' has a null service entry.");
            if (!seen.Add(s.ServiceId))
                return Error.Validation(
                    $"Service '{s.Name}' is listed more than once for operation type '{operationType.Name}'.");
            if (s.IsAog) anyAog = true;
            else anyNonAog = true;
        }

        if (anyAog && anyNonAog)
            return Error.Validation(
                $"Operation type '{operationType.Name}' may have either the AOG service alone "
                + "or any other set of services — not a mix.");

        return Result.Success();
    }
}
