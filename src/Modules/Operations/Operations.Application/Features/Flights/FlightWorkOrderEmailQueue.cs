using System.Net;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using Microsoft.Extensions.Logging;
using Operations.Application.Abstractions;
using Operations.Domain.Authorization;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public interface IFlightWorkOrderEmailQueue
{
    public Task<Result> EnqueueAsync(Flight flight, Guid actorUserId, CancellationToken cancellationToken);
}

public sealed class FlightWorkOrderEmailQueue(
    IOperationsDbContext db,
    IUserContext user,
    IWorkOrderEmailRecipientReader recipients,
    FlightWorkOrderPrintSource printSource,
    IFlightWorkOrderPdfRenderer renderer,
    IEmailContentProtector protector,
    ILogger<FlightWorkOrderEmailQueue> logger) : IFlightWorkOrderEmailQueue
{
    public async Task<Result> EnqueueAsync(Flight flight, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || user.UserId != actorUserId || !user.HasPermission(OperationsPermissions.Flights.View))
            return Result.Success();

        try
        {
            var recipient = await recipients.GetAsync(actorUserId, cancellationToken);
            if (recipient is not { ReceiveWorkOrderSubmissionEmails: true })
                return Result.Success();

            // Render the pending aggregate so delayed delivery preserves the version from this save.
            var source = await printSource.BuildAsync(flight, cancellationToken);
            if (source.IsFailure)
                return source.Error;

            var pdf = renderer.Render(source.Value);
            var flightNumber = flight.FlightNumber.Value;
            var safeSubjectNumber = new string(flightNumber.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
            var html = $"""
                <p>Hello {WebUtility.HtmlEncode(recipient.DisplayName)},</p>
                <p>The attached work order PDF for flight {WebUtility.HtmlEncode(flightNumber)} includes the changes you saved.</p>
                """;
            db.EnqueueEmail(protector, new EmailMessage(recipient.Email, recipient.DisplayName,
                $"Work order updated — {safeSubjectNumber}", html,
                [new EmailAttachment(pdf.FileName, "application/pdf", pdf.Content)]), kind: "work-order-submission");

            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not prepare the work order email for flight {FlightId}.", flight.Id);
            return Error.Failure("Could not prepare your work order email. The changes were not saved. Please try again.",
                "Operations.WorkOrder.SubmissionEmailFailed");
        }
    }
}
