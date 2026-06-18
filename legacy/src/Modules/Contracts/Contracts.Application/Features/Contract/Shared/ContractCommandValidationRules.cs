namespace Contracts.Application.Features.Contract.Shared;

/// <summary>
/// Shared FluentValidation predicates for the create / update contract command validators.
/// The Application-layer 422 surfaces clearer field-level errors before the aggregate's
/// generic <c>Error.Validation</c> kicks in.
/// </summary>
internal static class ContractCommandValidationRules
{
    /// <summary>
    /// True when no two service pricing inputs share the same
    /// (<c>OperationTypeId</c>, <c>ServiceId</c>, <c>AircraftTypeId</c>) tuple. Empty /
    /// null lists pass — service pricing is optional now.
    /// </summary>
    public static bool HaveUniqueServiceScope(IReadOnlyList<ContractServiceInput>? services)
    {
        if (services is null || services.Count == 0) return true;

        var seen = new HashSet<(Guid Ot, Guid Service, Guid? Aircraft)>();
        foreach (var s in services)
        {
            if (s is null) continue;
            var key = (s.OperationTypeId, s.ServiceId, s.AircraftTypeId);
            if (!seen.Add(key)) return false;
        }
        return true;
    }
}
