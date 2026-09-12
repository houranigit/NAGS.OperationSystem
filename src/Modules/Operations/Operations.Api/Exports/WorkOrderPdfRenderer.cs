using Operations.Application.Abstractions;
using Operations.Application.Contracts;

namespace Operations.Api.Exports;

public sealed class WorkOrderPdfRenderer : IWorkOrderPdfRenderer
{
    public WorkOrderPdfFile Create(ApprovedWorkOrderPrintDto source)
    {
        var file = WorkOrderPrintDocumentFactory.Create(source);
        return new WorkOrderPdfFile(file.Content, file.FileName);
    }
}
