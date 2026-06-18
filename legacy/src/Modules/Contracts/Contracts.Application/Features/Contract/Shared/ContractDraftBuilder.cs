using BuildingBlocks.Domain.Results;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Contracts.Domain.ValueObjects;
using Core.Contracts.Readers;
using Core.Contracts.Seeding;
using Store.Contracts.Readers;

namespace Contracts.Application.Features.Contract.Shared;

/// <summary>
/// Pulls every Core / Store reference, builds the snapshots & drafts the aggregate factory
/// expects, and surfaces the first validation/lookup failure as a <see cref="Result"/>.
/// Shared between <c>CreateContractCommandHandler</c> and <c>UpdateContractCommandHandler</c>.
/// </summary>
public sealed class ContractDraftBuilder(
    ICustomerReader customers,
    ICurrencyReader currencies,
    IOperationTypeReader operationTypes,
    IStationReader stations,
    IServiceReader services,
    IManpowerTypeReader manpowerTypes,
    IAircraftTypeReader aircraftTypes,
    IToolReader toolsReader,
    IMaterialReader materialsReader,
    IGeneralSupportReader generalSupportsReader,
    TimeProvider time)
{
    public async Task<Result<ResolvedContract>> BuildAsync(
        Guid customerId,
        Guid currencyId,
        IReadOnlyList<Guid> stationIds,
        IReadOnlyList<ContractOperationTypeInput> operationTypeInputs,
        IReadOnlyList<ContractServiceInput> serviceInputs,
        IReadOnlyList<ContractManpowerInput> manpowerInputs,
        IReadOnlyList<ContractToolInput> toolInputs,
        IReadOnlyList<ContractMaterialInput> materialInputs,
        IReadOnlyList<ContractGeneralSupportInput> generalSupportInputs,
        CancellationToken cancellationToken)
    {
        // -- Customer ---------------------------------------------------------
        if (!await customers.ExistsActiveAsync(customerId, cancellationToken))
            return Error.Validation("Customer not found or inactive.");
        var customerSnap = await customers.GetByIdAsync(customerId, cancellationToken);
        if (customerSnap is null)
            return Error.Validation("Customer not found.");
        var customerResult = CustomerSnapshot.Create(
            customerSnap.CustomerId, customerSnap.IataCode, customerSnap.Name);
        if (customerResult.IsFailure) return customerResult.Error;

        // -- Currency + exchange rate ----------------------------------------
        if (!await currencies.ExistsActiveAsync(currencyId, cancellationToken))
            return Error.Validation("Currency not found or inactive.");
        var currencySnap = await currencies.GetByIdAsync(currencyId, cancellationToken);
        if (currencySnap is null)
            return Error.Validation("Currency not found.");
        var currencyResult = CurrencySnapshot.Create(currencySnap.CurrencyId, currencySnap.Code);
        if (currencyResult.IsFailure) return currencyResult.Error;

        if (!await currencies.HasRateToAsync(
                currencyId, CoreSeedIds.SarCurrency, time.GetUtcNow().UtcDateTime, cancellationToken))
            return Error.Validation(
                "Selected currency must have an exchange rate to the platform currency on or before today.");

        // -- Stations --------------------------------------------------------
        var distinctStationIds = stationIds.Distinct().ToList();
        var inactiveStations = await stations.GetInactiveOrMissingIdsAsync(distinctStationIds, cancellationToken);
        if (inactiveStations.Count > 0)
            return Error.Validation(
                $"One or more stations are inactive or do not exist: {string.Join(", ", inactiveStations)}.");
        var stationSnaps = await stations.GetManyAsync(distinctStationIds, cancellationToken);
        var stationDomainSnaps = new List<StationSnapshot>(stationSnaps.Count);
        foreach (var s in stationSnaps)
        {
            var snap = StationSnapshot.Create(s.StationId, s.IataCode, s.Name);
            if (snap.IsFailure) return snap.Error;
            stationDomainSnaps.Add(snap.Value);
        }

        // -- Operation types -------------------------------------------------
        var distinctOpIds = operationTypeInputs.Select(o => o.OperationTypeId).Distinct().ToList();
        if (distinctOpIds.Count != operationTypeInputs.Count)
            return Error.Validation("Operation types must be unique.");
        if (distinctOpIds.Contains(CoreSeedIds.AdHocOperationType))
            return Error.Validation("Contracts cannot use the Ad Hoc operation type.");
        var operationTypeSnaps = new Dictionary<Guid, OperationTypeSnapshot>();
        foreach (var opId in distinctOpIds)
        {
            if (!await operationTypes.ExistsActiveAsync(opId, cancellationToken))
                return Error.Validation($"Operation type '{opId}' is inactive or does not exist.");
            var snap = await operationTypes.GetByIdAsync(opId, cancellationToken);
            if (snap is null) return Error.Validation($"Operation type '{opId}' not found.");
            var built = OperationTypeSnapshot.Create(snap.OperationTypeId, snap.Name);
            if (built.IsFailure) return built.Error;
            operationTypeSnaps[opId] = built.Value;
        }

        // -- Services --------------------------------------------------------
        // Union of services referenced from the OT step AND the optional pricing rows.
        var allServiceIds = new HashSet<Guid>();
        foreach (var ot in operationTypeInputs)
            foreach (var sid in ot.ServiceIds)
                allServiceIds.Add(sid);
        foreach (var s in serviceInputs)
            allServiceIds.Add(s.ServiceId);

        var distinctServiceIds = allServiceIds.ToList();
        var inactiveServices = await services.GetInactiveOrMissingIdsAsync(distinctServiceIds, cancellationToken);
        if (inactiveServices.Count > 0)
            return Error.Validation(
                $"One or more services are inactive or do not exist: {string.Join(", ", inactiveServices)}.");
        var serviceSnaps = (await services.GetManyAsync(distinctServiceIds, cancellationToken))
            .ToDictionary(s => s.ServiceId);

        // -- Build operation-type drafts (OT + applicable contract services) -
        var operationTypeDrafts = new List<ContractOperationTypeDraft>(operationTypeInputs.Count);
        foreach (var ot in operationTypeInputs)
        {
            if (!operationTypeSnaps.TryGetValue(ot.OperationTypeId, out var opSnap))
                return Error.Validation($"Operation type '{ot.OperationTypeId}' not found.");

            if (ot.ServiceIds is null || ot.ServiceIds.Count == 0)
                return Error.Validation($"Operation type '{opSnap.Name}' must have at least 1 service.");

            var seen = new HashSet<Guid>();
            var snaps = new List<ServiceSnapshot>(ot.ServiceIds.Count);
            foreach (var sid in ot.ServiceIds)
            {
                if (!seen.Add(sid))
                    return Error.Validation(
                        $"Service '{sid}' is listed more than once for operation type '{opSnap.Name}'.");
                if (!serviceSnaps.TryGetValue(sid, out var raw))
                    return Error.Validation($"Service '{sid}' not found.");
                var snap = ServiceSnapshot.Create(raw.ServiceId, raw.Name, isAog: raw.ServiceId == CoreSeedIds.AogService);
                if (snap.IsFailure) return snap.Error;
                snaps.Add(snap.Value);
            }
            operationTypeDrafts.Add(new ContractOperationTypeDraft(opSnap, snaps));
        }

        // -- Manpower types --------------------------------------------------
        var distinctMpIds = manpowerInputs.Select(m => m.ManpowerTypeId).Distinct().ToList();
        if (distinctMpIds.Count > 0)
        {
            var inactiveMps = await manpowerTypes.GetInactiveOrMissingIdsAsync(distinctMpIds, cancellationToken);
            if (inactiveMps.Count > 0)
                return Error.Validation(
                    $"One or more manpower types are inactive or do not exist: {string.Join(", ", inactiveMps)}.");
        }
        var manpowerSnaps = (await manpowerTypes.GetManyAsync(distinctMpIds, cancellationToken))
            .ToDictionary(m => m.ManpowerTypeId);

        // -- Tools (Store) ---------------------------------------------------
        var distinctToolIds = toolInputs.Select(t => t.ToolId).Distinct().ToList();
        foreach (var tid in distinctToolIds)
        {
            if (!await toolsReader.ExistsActiveAsync(tid, cancellationToken))
                return Error.Validation($"Tool '{tid}' is inactive or does not exist.");
        }
        var toolSnaps = (await toolsReader.GetManyAsync(distinctToolIds, cancellationToken))
            .ToDictionary(t => t.ToolId);

        // -- Materials (Store) -----------------------------------------------
        var distinctMaterialIds = materialInputs.Select(m => m.MaterialId).Distinct().ToList();
        foreach (var mid in distinctMaterialIds)
        {
            if (!await materialsReader.ExistsActiveAsync(mid, cancellationToken))
                return Error.Validation($"Material '{mid}' is inactive or does not exist.");
        }
        var materialSnaps = (await materialsReader.GetManyAsync(distinctMaterialIds, cancellationToken))
            .ToDictionary(m => m.MaterialId);

        // -- General supports (Store) ---------------------------------------
        var distinctGeneralSupportIds = generalSupportInputs.Select(g => g.GeneralSupportId).Distinct().ToList();
        foreach (var gid in distinctGeneralSupportIds)
        {
            if (!await generalSupportsReader.ExistsActiveAsync(gid, cancellationToken))
                return Error.Validation($"General-support '{gid}' is inactive or does not exist.");
        }
        var generalSupportSnaps = (await generalSupportsReader.GetManyAsync(distinctGeneralSupportIds, cancellationToken))
            .ToDictionary(g => g.GeneralSupportId);

        // -- Aircraft types (optional dimension) -----------------------------
        var distinctAircraftIds = serviceInputs
            .Where(s => s.AircraftTypeId.HasValue).Select(s => s.AircraftTypeId!.Value)
            .Concat(toolInputs.Where(t => t.AircraftTypeId.HasValue).Select(t => t.AircraftTypeId!.Value))
            .Distinct()
            .ToList();
        var aircraftSnaps = new Dictionary<Guid, AircraftTypeSnapshot>();
        foreach (var aId in distinctAircraftIds)
        {
            if (!await aircraftTypes.ExistsActiveAsync(aId, cancellationToken))
                return Error.Validation($"Aircraft type '{aId}' is inactive or does not exist.");
            var snap = await aircraftTypes.GetByIdAsync(aId, cancellationToken);
            if (snap is null) return Error.Validation($"Aircraft type '{aId}' not found.");
            var built = AircraftTypeSnapshot.Create(snap.AircraftTypeId, snap.Model);
            if (built.IsFailure) return built.Error;
            aircraftSnaps[aId] = built.Value;
        }

        // -- Build service drafts -------------------------------------------
        var serviceDrafts = new List<ContractServiceDraft>(serviceInputs.Count);
        foreach (var input in serviceInputs)
        {
            if (!operationTypeSnaps.TryGetValue(input.OperationTypeId, out var opSnap))
                return Error.Validation($"Service references operation type '{input.OperationTypeId}' which is not part of the contract.");
            if (!serviceSnaps.TryGetValue(input.ServiceId, out var raw))
                return Error.Validation($"Service '{input.ServiceId}' not found.");
            var svcSnapResult = ServiceSnapshot.Create(
                raw.ServiceId, raw.Name, isAog: raw.ServiceId == CoreSeedIds.AogService);
            if (svcSnapResult.IsFailure) return svcSnapResult.Error;

            AircraftTypeSnapshot? aircraftSnap = null;
            if (input.AircraftTypeId.HasValue)
            {
                if (!aircraftSnaps.TryGetValue(input.AircraftTypeId.Value, out var snap))
                    return Error.Validation($"Aircraft type '{input.AircraftTypeId.Value}' not found.");
                aircraftSnap = snap;
            }

            var brackets = DomainMappers.ToBrackets(input.Brackets);
            if (brackets.IsFailure) return brackets.Error;
            var packageBalance = DomainMappers.ToOptionalMoney(input.PackagePaidBalance);
            if (packageBalance.IsFailure) return packageBalance.Error;

            serviceDrafts.Add(new ContractServiceDraft(
                opSnap, svcSnapResult.Value, aircraftSnap, input.Basis,
                packageBalance.Value, brackets.Value, input.ExistingContractServiceId));
        }

        // -- Build manpower drafts ------------------------------------------
        var manpowerDrafts = new List<ContractManpowerDraft>(manpowerInputs.Count);
        foreach (var input in manpowerInputs)
        {
            if (!operationTypeSnaps.TryGetValue(input.OperationTypeId, out var opSnap))
                return Error.Validation($"Manpower references operation type '{input.OperationTypeId}' which is not part of the contract.");
            if (!manpowerSnaps.TryGetValue(input.ManpowerTypeId, out var raw))
                return Error.Validation($"Manpower type '{input.ManpowerTypeId}' not found.");
            var mpSnap = ManpowerTypeSnapshot.Create(raw.ManpowerTypeId, raw.Name);
            if (mpSnap.IsFailure) return mpSnap.Error;

            var brackets = DomainMappers.ToBrackets(input.Brackets);
            if (brackets.IsFailure) return brackets.Error;
            var packageBalance = DomainMappers.ToOptionalMoney(input.PackagePaidBalance);
            if (packageBalance.IsFailure) return packageBalance.Error;

            manpowerDrafts.Add(new ContractManpowerDraft(
                opSnap, mpSnap.Value, input.Basis, packageBalance.Value, brackets.Value,
                input.ExistingContractManpowerId));
        }

        // -- Build tool drafts ----------------------------------------------
        var toolDrafts = new List<ContractToolDraft>(toolInputs.Count);
        foreach (var input in toolInputs)
        {
            if (!operationTypeSnaps.TryGetValue(input.OperationTypeId, out var opSnap))
                return Error.Validation($"Tool references operation type '{input.OperationTypeId}' which is not part of the contract.");
            if (!toolSnaps.TryGetValue(input.ToolId, out var raw))
                return Error.Validation($"Tool '{input.ToolId}' not found.");
            var toolSnapResult = ToolSnapshot.Create(raw.ToolId, raw.Name);
            if (toolSnapResult.IsFailure) return toolSnapResult.Error;

            AircraftTypeSnapshot? aircraftSnap = null;
            if (input.AircraftTypeId.HasValue)
            {
                if (!aircraftSnaps.TryGetValue(input.AircraftTypeId.Value, out var snap))
                    return Error.Validation($"Aircraft type '{input.AircraftTypeId.Value}' not found.");
                aircraftSnap = snap;
            }

            var brackets = DomainMappers.ToBrackets(input.Brackets);
            if (brackets.IsFailure) return brackets.Error;
            var packageBalance = DomainMappers.ToOptionalMoney(input.PackagePaidBalance);
            if (packageBalance.IsFailure) return packageBalance.Error;

            toolDrafts.Add(new ContractToolDraft(
                opSnap, toolSnapResult.Value, aircraftSnap, input.Basis,
                packageBalance.Value, brackets.Value, input.ExistingContractToolId));
        }

        // -- Build material drafts ------------------------------------------
        var materialDrafts = new List<ContractMaterialDraft>(materialInputs.Count);
        foreach (var input in materialInputs)
        {
            if (!operationTypeSnaps.TryGetValue(input.OperationTypeId, out var opSnap))
                return Error.Validation($"Material references operation type '{input.OperationTypeId}' which is not part of the contract.");
            if (!materialSnaps.TryGetValue(input.MaterialId, out var raw))
                return Error.Validation($"Material '{input.MaterialId}' not found.");
            var matSnapResult = MaterialSnapshot.Create(raw.MaterialId, raw.Name);
            if (matSnapResult.IsFailure) return matSnapResult.Error;

            var brackets = DomainMappers.ToBrackets(input.Brackets);
            if (brackets.IsFailure) return brackets.Error;
            var packageBalance = DomainMappers.ToOptionalMoney(input.PackagePaidBalance);
            if (packageBalance.IsFailure) return packageBalance.Error;

            materialDrafts.Add(new ContractMaterialDraft(
                opSnap, matSnapResult.Value, input.Basis,
                packageBalance.Value, brackets.Value, input.ExistingContractMaterialId));
        }

        // -- Build general-support drafts -----------------------------------
        var generalSupportDrafts = new List<ContractGeneralSupportDraft>(generalSupportInputs.Count);
        foreach (var input in generalSupportInputs)
        {
            if (!operationTypeSnaps.TryGetValue(input.OperationTypeId, out var opSnap))
                return Error.Validation($"General-support references operation type '{input.OperationTypeId}' which is not part of the contract.");
            if (!generalSupportSnaps.TryGetValue(input.GeneralSupportId, out var raw))
                return Error.Validation($"General-support '{input.GeneralSupportId}' not found.");
            var gsSnapResult = GeneralSupportSnapshot.Create(raw.GeneralSupportId, raw.Name);
            if (gsSnapResult.IsFailure) return gsSnapResult.Error;

            var brackets = DomainMappers.ToBrackets(input.Brackets);
            if (brackets.IsFailure) return brackets.Error;
            var packageBalance = DomainMappers.ToOptionalMoney(input.PackagePaidBalance);
            if (packageBalance.IsFailure) return packageBalance.Error;

            generalSupportDrafts.Add(new ContractGeneralSupportDraft(
                opSnap, gsSnapResult.Value, input.Basis,
                packageBalance.Value, brackets.Value, input.ExistingContractGeneralSupportId));
        }

        return new ResolvedContract(
            customerResult.Value,
            currencyResult.Value,
            stationDomainSnaps,
            operationTypeDrafts,
            serviceDrafts,
            manpowerDrafts,
            toolDrafts,
            materialDrafts,
            generalSupportDrafts);
    }
}

/// <summary>
/// Snapshot bundle returned by <see cref="ContractDraftBuilder.BuildAsync"/>. Contains every
/// fully-resolved cross-module reference plus the per-line drafts the aggregate factory
/// consumes.
/// </summary>
public sealed record ResolvedContract(
    CustomerSnapshot Customer,
    CurrencySnapshot Currency,
    IReadOnlyList<StationSnapshot> Stations,
    IReadOnlyList<ContractOperationTypeDraft> OperationTypes,
    IReadOnlyList<ContractServiceDraft> Services,
    IReadOnlyList<ContractManpowerDraft> Manpowers,
    IReadOnlyList<ContractToolDraft> Tools,
    IReadOnlyList<ContractMaterialDraft> Materials,
    IReadOnlyList<ContractGeneralSupportDraft> GeneralSupports);
