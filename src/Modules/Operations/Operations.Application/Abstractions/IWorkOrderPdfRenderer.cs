using Operations.Application.Contracts;

namespace Operations.Application.Abstractions;

public sealed record WorkOrderPdfFile(byte[] Content, string FileName);

/// <summary>Renders an immutable work-order snapshot with the shared printable layout.</summary>
public interface IWorkOrderPdfRenderer
{
    public WorkOrderPdfFile Create(ApprovedWorkOrderPrintDto source);
}
