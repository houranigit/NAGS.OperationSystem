using System.Net;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using Microsoft.Extensions.Logging;
using Operations.Application.Abstractions;
using Operations.Domain.Flights;
using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public interface IWorkOrderSubmissionEmailQueue
{
    public Task<Result> EnqueueAsync(WorkOrder workOrder, Flight flight, Guid submittedByUserId, CancellationToken cancellationToken);
}

/// <summary>
/// Captures the submitted PDF before the caller saves its aggregate and outbox together. Delivery
/// never reads mutable work-order data, and a rendering failure prevents an incomplete receipt.
/// </summary>
public sealed class WorkOrderSubmissionEmailQueue(
    IOperationsDbContext db,
    IWorkOrderEmailRecipientReader recipients,
    WorkOrderPrintSourceBuilder printSource,
    IWorkOrderPdfRenderer renderer,
    IEmailContentProtector protector,
    ILogger<WorkOrderSubmissionEmailQueue> logger) : IWorkOrderSubmissionEmailQueue
{
    public async Task<Result> EnqueueAsync(
        WorkOrder workOrder,
        Flight flight,
        Guid submittedByUserId,
        CancellationToken cancellationToken)
    {
        if (workOrder.Status != WorkOrderStatus.Submitted || workOrder.IsMergeGenerated ||
            submittedByUserId == Guid.Empty || workOrder.OwnerUserId != submittedByUserId)
            return Result.Success();

        try
        {
            var recipient = await recipients.GetAsync(workOrder.OwnerUserId, cancellationToken);
            if (recipient is null || !recipient.ReceiveWorkOrderSubmissionEmails)
                return Result.Success();

            var source = await printSource.BuildAsync(workOrder, flight, cancellationToken);
            var pdf = renderer.Create(source);
            if (pdf.Content.Length == 0)
                throw new InvalidOperationException("The work order PDF was empty.");

            var html = $"""
                <p>Hello {WebUtility.HtmlEncode(recipient.DisplayName)},</p>
                <p>Your {workOrder.Type.ToString().ToLowerInvariant()} work order for flight {WebUtility.HtmlEncode(workOrder.ActualFlightNumber.Value)} at {WebUtility.HtmlEncode(workOrder.Station.Name)} was submitted.</p>
                <p>The attached PDF is a copy of the work order as submitted.</p>
                <p>Work order reference: {workOrder.Id:D}</p>
                """;
            db.EnqueueEmail(protector, new EmailMessage(
                recipient.Email,
                recipient.DisplayName,
                $"Work order submitted — {NormalizeSubjectValue(workOrder.ActualFlightNumber.Value)}",
                html,
                [new EmailAttachment(pdf.FileName, "application/pdf", pdf.Content)]),
                kind: "work-order-submission");

            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not prepare submission email for work order {WorkOrderId}.", workOrder.Id);
            return Error.Failure(
                "Could not prepare your work order email. The submission was not saved; please try again.",
                "Operations.WorkOrder.SubmissionEmailFailed");
        }
    }

    private static string NormalizeSubjectValue(string value) =>
        new string(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
}
