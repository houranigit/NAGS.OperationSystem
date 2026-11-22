using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed record PortalReturnToRampDraft(Guid Id, int Sequence, PortalReturnToRampRequest Request, string? SignatureFileName)
{
    public bool IsOpen => Request.Departure is null;
}
