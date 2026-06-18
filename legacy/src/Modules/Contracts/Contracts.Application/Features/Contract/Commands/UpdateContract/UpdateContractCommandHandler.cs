using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Contracts.Application.Features.Contract.Shared;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.ValueObjects;
using Core.Contracts.Seeding;

namespace Contracts.Application.Features.Contract.Commands.UpdateContract;

public sealed class UpdateContractCommandHandler(
    IContractRepository contracts,
    ContractDraftBuilder builder,
    ICurrentUserService currentUser,
    TimeProvider time)
    : ICommandHandler<UpdateContractCommand>
{
    public async Task<Result> Handle(UpdateContractCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Error.Unauthorized("Authenticated user is required to update a contract.");

        var contractId = ContractId.From(request.Id);
        var contract = await contracts.GetByIdWithDetailsAsync(contractId, cancellationToken);
        if (contract is null)
            return Error.NotFound("Contract not found.");

        var contractNoResult = ContractNo.Create(request.ContractNo);
        if (contractNoResult.IsFailure) return contractNoResult.Error;
        if (await contracts.ExistsByContractNoAsync(contractNoResult.Value.Value, contractId, cancellationToken))
            return Error.Conflict("A contract with this number already exists.");

        var periodResult = DomainMappers.ToContractPeriod(request.Period);
        if (periodResult.IsFailure) return periodResult.Error;

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

        if (await contracts.HasActiveOverlapAsync(
                request.CustomerId,
                request.OperationTypes.Select(o => o.OperationTypeId).Distinct().ToArray(),
                request.StationIds.Distinct().ToArray(),
                request.Period.StartDate,
                request.Period.ExpiryDate,
                excludeContractId: contractId,
                cancellationToken))
        {
            return Error.Conflict(
                "Another active contract for this customer already covers at least one of the "
                + "selected stations and operation types in an overlapping period.");
        }

        var feesAndRates = DomainMappers.ToFeesAndRates(request.FeesAndRates);
        if (feesAndRates.IsFailure) return feesAndRates.Error;
        var advanceDrafts = DomainMappers.ToAdvancePayments(request.AdvancePayments);
        if (advanceDrafts.IsFailure) return advanceDrafts.Error;
        var cancellation = DomainMappers.ToCancellationPlan(request.Cancellation);
        if (cancellation.IsFailure) return cancellation.Error;
        var delay = DomainMappers.ToDelayPlan(request.Delay);
        if (delay.IsFailure) return delay.Error;

        var updateResult = contract.Update(
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
            updatedByUserId: userId.Value,
            now: time.GetUtcNow());
        if (updateResult.IsFailure) return updateResult.Error;

        contracts.Update(contract);
        return Result.Success();
    }
}
