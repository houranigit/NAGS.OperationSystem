using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Contracts.Application.Features.Contract.Shared;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.ValueObjects;
using Core.Contracts.Seeding;

namespace Contracts.Application.Features.Contract.Commands.CreateContract;

/// <summary>
/// Single-shot wizard handler. Resolves every Core / Store reference, runs the no-overlap
/// check, then asks the aggregate to atomically validate and instantiate. <c>TransactionBehavior</c>
/// commits and dispatches domain events.
/// </summary>
public sealed class CreateContractCommandHandler(
    IContractRepository contracts,
    ContractDraftBuilder builder,
    ICurrentUserService currentUser,
    TimeProvider time)
    : ICommandHandler<CreateContractCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateContractCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Error.Unauthorized("Authenticated user is required to create a contract.");

        // -- ContractNo + uniqueness ----------------------------------------
        var contractNoResult = ContractNo.Create(request.ContractNo);
        if (contractNoResult.IsFailure) return contractNoResult.Error;
        if (await contracts.ExistsByContractNoAsync(contractNoResult.Value.Value, null, cancellationToken))
            return Error.Conflict("A contract with this number already exists.");

        // -- Period ----------------------------------------------------------
        var periodResult = DomainMappers.ToContractPeriod(request.Period);
        if (periodResult.IsFailure) return periodResult.Error;

        // -- Resolve all Core / Store refs + build drafts -------------------
        var resolved = await builder.BuildAsync(
            request.CustomerId,
            request.CurrencyId,
            request.StationIds,
            request.OperationTypes,
            request.Services,
            request.Manpowers,
            request.Tools,
            request.Materials,
            request.GeneralSupports,
            cancellationToken);
        if (resolved.IsFailure) return resolved.Error;

        // -- Overlap check (rule #5) ----------------------------------------
        if (await contracts.HasActiveOverlapAsync(
                request.CustomerId,
                request.OperationTypes.Select(o => o.OperationTypeId).Distinct().ToArray(),
                request.StationIds.Distinct().ToArray(),
                request.Period.StartDate,
                request.Period.ExpiryDate,
                excludeContractId: null,
                cancellationToken))
        {
            return Error.Conflict(
                "An active contract already exists for this customer that shares at least "
                + "one station and one operation type with an overlapping period.");
        }

        // -- VOs from the wizard payload ------------------------------------
        var feesAndRates = DomainMappers.ToFeesAndRates(request.FeesAndRates);
        if (feesAndRates.IsFailure) return feesAndRates.Error;
        var advanceDrafts = DomainMappers.ToAdvancePayments(request.AdvancePayments);
        if (advanceDrafts.IsFailure) return advanceDrafts.Error;
        var cancellation = DomainMappers.ToCancellationPlan(request.Cancellation);
        if (cancellation.IsFailure) return cancellation.Error;
        var delay = DomainMappers.ToDelayPlan(request.Delay);
        if (delay.IsFailure) return delay.Error;

        var created = global::Contracts.Domain.Aggregates.Contract.Contract.Create(
            contractNoResult.Value,
            resolved.Value.Customer,
            resolved.Value.Currency,
            periodResult.Value,
            request.PaymentTerms,
            request.ApplyVat,
            request.DebriefRequired,
            request.Attachment,
            feesAndRates.Value,
            advanceDrafts.Value,
            cancellation.Value,
            delay.Value,
            resolved.Value.Stations,
            resolved.Value.OperationTypes,
            resolved.Value.Services,
            resolved.Value.Manpowers,
            resolved.Value.Tools,
            resolved.Value.Materials,
            resolved.Value.GeneralSupports,
            adHocOperationTypeId: CoreSeedIds.AdHocOperationType,
            createdByUserId: userId.Value,
            now: time.GetUtcNow());
        if (created.IsFailure) return created.Error;

        contracts.Add(created.Value);
        return created.Value.Id.Value;
    }
}
