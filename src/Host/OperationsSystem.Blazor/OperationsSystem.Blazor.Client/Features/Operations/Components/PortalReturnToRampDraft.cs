using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed record PortalReturnToRampDraft(Guid Id, int Sequence, PortalReturnToRampRequest Request, string? SignatureFileName)
{
    internal PortalWorkLookups? Lookups { get; init; }

    public bool IsOpen => Request.Departure is null;

    internal (List<PortalWorkDraft> Services, List<PortalWorkDraft> Tasks) ReadWorkRecords(
        StationTimeZone zone, PortalWorkLookups lookups)
    {
        var services = new List<PortalWorkDraft>();
        foreach (var change in Request.Services)
        {
            if (change.Input is not { } value)
                continue;

            var work = new PortalWorkDraft(change.RecordId, false, zone, value.StartUtc, value.EndUtc)
            {
                ServiceId = value.ServiceId, Reference = value.CustomerReferenceNumber, Notes = value.Notes
            };
            ReadWork(work, value.Performers, value.Attachments);
            services.Add(work);
        }

        var tasks = new List<PortalWorkDraft>();
        foreach (var change in Request.Tasks)
        {
            if (change.Input is not { } value)
                continue;

            var work = new PortalWorkDraft(change.RecordId, true, zone, value.StartUtc, value.EndUtc)
            {
                TaskType = value.TaskType, AtaChapterId = value.AtaChapterId, Reference = value.CustomerReferenceNumber, Notes = value.Notes
            };
            ReadWork(work, value.Performers, value.Attachments);
            work.Tools.AddRange(ReadResources(value.Tools, lookups.Tools));
            work.Materials.AddRange(ReadResources(value.Materials, lookups.Materials));
            work.GeneralSupports.AddRange(ReadResources(value.GeneralSupports, lookups.Supports));
            tasks.Add(work);
        }

        return (services, tasks);

        void ReadWork(PortalWorkDraft work, IReadOnlyList<PortalFlightPerformerRequest> performers,
            IReadOnlyList<PortalFlightAttachmentRequest> attachments)
        {
            foreach (var performer in performers)
            {
                var staff = lookups.Staff.FirstOrDefault(choice => choice.Id == performer.StaffMemberId);
                work.Performers.Add(new(performer.Id, zone, performer.StartUtc, performer.EndUtc)
                {
                    StaffMemberId = performer.StaffMemberId, DisplayName = staff?.Label, FullName = staff?.FullName, Notes = performer.Notes
                });
            }
            work.Attachments.AddRange(attachments.Select(file =>
                new PortalAttachmentDraft(file.Id, file.Kind, file.FileName, file.ContentType, file.Content?.LongLength ?? 0, null, file.Content)));
        }

        IEnumerable<PortalResourceDraft> ReadResources(IReadOnlyList<PortalFlightResourceRequest> values, IReadOnlyList<PortalCatalogChoice> options) =>
            values.Select(value => new PortalResourceDraft(value.Id, value.ResourceId,
                options.FirstOrDefault(choice => choice.Id == value.ResourceId)?.Label ?? "Saved resource",
                value.Quantity is null ? ResourceCalculationType.Duration : ResourceCalculationType.Quantity, zone, value.StartUtc, value.EndUtc)
            {
                Quantity = value.Quantity, Notes = value.Notes
            });
    }
}
