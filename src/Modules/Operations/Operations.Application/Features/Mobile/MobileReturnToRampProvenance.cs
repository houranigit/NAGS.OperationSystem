using BuildingBlocks.Domain.Results;
using Operations.Application.Features.WorkOrders;

namespace Operations.Application.Features.Mobile;

/// <summary>
/// The mobile create/update routes are not allowed to author return-to-ramp provenance. New rows
/// are always normal; existing rows inherit the value already stored for their server identity.
/// Only the dedicated return-to-ramp command may create rows whose flag is true.
/// </summary>
internal static class MobileReturnToRampProvenance
{
    public const int StableServiceLineIdentityVersion = 1;

    public static WorkOrderEditableCommandPayload ProtectNew(WorkOrderEditableCommandPayload payload) =>
        payload with
        {
            ServiceLines = (payload.ServiceLines ?? [])
                .Select(line => line with { Id = null, IsReturnToRamp = false })
                .ToList(),
            Tasks = (payload.Tasks ?? [])
                .Select(task => task with { Id = null, IsReturnToRamp = false })
                .ToList()
        };

    public static Result<WorkOrderEditableCommandPayload> ProtectUpdate(
        WorkOrderEditableCommandPayload payload,
        IReadOnlyDictionary<Guid, bool> persistedServiceLineFlags,
        IReadOnlyDictionary<Guid, bool> persistedTaskFlags,
        int serviceLineIdentityVersion,
        bool hasPersistedServiceLineAttachments = false)
    {
        var serviceLines = payload.ServiceLines ?? [];
        var suppliedServiceLineIds = serviceLines
            .Where(line => line.Id.HasValue)
            .Select(line => line.Id!.Value)
            .ToList();

        if (suppliedServiceLineIds.Count != suppliedServiceLineIds.Distinct().Count())
        {
            return Error.Validation(
                "Service line ids must be unique.",
                "Operations.Mobile.ServiceLineIdsDuplicate");
        }

        if (suppliedServiceLineIds.Any(id => !persistedServiceLineFlags.ContainsKey(id)))
        {
            return Error.Conflict(
                "One or more service line ids do not belong to this work order.",
                "Operations.Mobile.ServiceLineIdForeign");
        }

        // Older app builds discarded service-line ids while editing. Once an order contains RTR
        // rows, accepting a non-empty full replacement from such a build could silently relabel an
        // RTR row as normal. Fail safely and let the device refresh with the identity-aware schema.
        if (serviceLineIdentityVersion < StableServiceLineIdentityVersion &&
            serviceLines.Count > 0 &&
            (persistedServiceLineFlags.Values.Any(isReturnToRamp => isReturnToRamp) ||
             hasPersistedServiceLineAttachments))
        {
            return Error.Conflict(
                "Refresh this work order in the latest mobile app before updating services with retained server state.",
                "Operations.Mobile.ServiceLineIdentityRequired");
        }

        var protectedServiceLines = serviceLines
            .Select(line => line with
            {
                IsReturnToRamp = line.Id is { } id && persistedServiceLineFlags[id]
            })
            .ToList();

        var protectedTasks = (payload.Tasks ?? [])
            .Select(task => task with
            {
                IsReturnToRamp = task.Id is { } id &&
                    persistedTaskFlags.TryGetValue(id, out var persistedFlag) &&
                    persistedFlag
            })
            .ToList();

        return payload with
        {
            ServiceLines = protectedServiceLines,
            Tasks = protectedTasks
        };
    }
}
