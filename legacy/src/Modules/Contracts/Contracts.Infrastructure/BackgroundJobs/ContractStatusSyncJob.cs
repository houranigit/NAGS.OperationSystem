using BuildingBlocks.Application.Abstractions;
using Contracts.Domain.Aggregates.Contract;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Contracts.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodic Quartz job that walks every non-terminal contract whose stored status no longer
/// matches its period and asks the aggregate to sync. Domain events flow to the outbox via
/// <c>BaseDbContext.SaveChangesAsync</c>.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ContractStatusSyncJob(
    IContractRepository contracts,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<ContractStatusSyncJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = time.GetUtcNow();
        var candidates = await contracts.GetStatusSyncCandidatesAsync(now, context.CancellationToken);
        if (candidates.Count == 0) return;

        var changed = 0;
        foreach (var contract in candidates)
        {
            var before = contract.Status;
            var result = contract.SyncAutomaticStatus(now);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Contract {ContractId} status sync failed: {Error}",
                    contract.Id.Value, result.Error.Description);
                continue;
            }
            if (contract.Status != before) changed++;
        }

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(context.CancellationToken);
    }
}
