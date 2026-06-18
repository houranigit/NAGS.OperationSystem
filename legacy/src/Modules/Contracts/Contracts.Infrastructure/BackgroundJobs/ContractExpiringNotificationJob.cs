using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using Contracts.Contracts.IntegrationEvents;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Enumerations;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Contracts.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodic Quartz job that publishes <see cref="ContractExpiringSoonIntegrationEvent"/>
/// outbox entries for active contracts whose expiry falls in the configured alert window.
/// Honours the per-contract <c>ExpiryAlertInterval</c> cadence so the same contract isn't
/// re-notified every poll cycle.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ContractExpiringNotificationJob(
    IContractRepository contracts,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<ContractExpiringNotificationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = time.GetUtcNow();
        var candidates = await contracts.GetExpiringSoonAsync(now, context.CancellationToken);
        if (candidates.Count == 0) return;

        var raised = 0;
        foreach (var contract in candidates)
        {
            if (!ShouldNotify(contract, now)) continue;

            var integrationEvent = new ContractExpiringSoonIntegrationEvent(
                contract.Id.Value,
                contract.ContractNo.Value,
                contract.CustomerId,
                contract.Period.ExpiryDate,
                Math.Max(0, (int)Math.Ceiling((contract.Period.ExpiryDate - now).TotalDays)));

            outbox.Write(
                eventType: nameof(ContractExpiringSoonIntegrationEvent),
                content: JsonSerializer.Serialize(integrationEvent));

            contract.MarkExpiringSoonNotified(now.UtcDateTime);
            raised++;
        }

        if (raised > 0)
        {
            await unitOfWork.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation("Raised {Count} contract expiring-soon notifications.", raised);
        }
    }

    private static bool ShouldNotify(global::Contracts.Domain.Aggregates.Contract.Contract contract, DateTimeOffset now)
    {
        if (contract.Status != ContractStatus.Active) return false;
        if (contract.LastExpiringSoonNotificationAt is null) return true;

        var elapsed = (now.UtcDateTime - contract.LastExpiringSoonNotificationAt.Value);
        var cadence = (contract.Period.ExpiryAlertInterval ?? ExpiryAlertInterval.Daily) switch
        {
            ExpiryAlertInterval.Daily => TimeSpan.FromDays(1),
            ExpiryAlertInterval.Weekly => TimeSpan.FromDays(7),
            ExpiryAlertInterval.Monthly => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(1)
        };
        return elapsed >= cadence;
    }
}
