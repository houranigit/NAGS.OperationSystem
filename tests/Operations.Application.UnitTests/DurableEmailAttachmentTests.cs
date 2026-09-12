using System.Text;
using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using BuildingBlocks.Contracts.Email;
using BuildingBlocks.Contracts.Messaging;
using BuildingBlocks.Infrastructure.Email;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class DurableEmailAttachmentTests
{
    [Fact]
    public async Task Queued_attachments_are_encrypted_snapshots_and_survive_delivery_retry()
    {
        await using var db = CreateDb();
        var protector = new DataProtectionEmailContentProtector(new EphemeralDataProtectionProvider());
        var submittedPdf = Encoding.UTF8.GetBytes("%PDF submitted work-order snapshot");
        var originalPdf = submittedPdf.ToArray();
        db.EnqueueEmail(protector, new EmailMessage("owner@example.test", "Owner", "Submitted", "<p>Private submitted details</p>",
            [new("WO-submitted.pdf", "application/pdf", submittedPdf)]), "work-order-submission");
        await db.SaveChangesAsync();
        var message = await db.OutboxMessages.SingleAsync();
        message.Content.ShouldNotContain("Private submitted details");
        message.Content.ShouldNotContain(Convert.ToBase64String(originalPdf));
        message.Content.ShouldNotContain("WO-submitted.pdf");

        Array.Fill(submittedPdf, (byte)0);
        var sender = new RetrySender();
        var handler = new EmailDeliveryRequestedHandler(sender, protector);
        var processor = new OutboxProcessor<OperationsDbContext>(db, new EmailDispatcher(handler), TimeProvider.System,
            NullLogger<OutboxProcessor<OperationsDbContext>>.Instance);

        await processor.ProcessAsync();

        message.ProcessedOnUtc.ShouldBeNull();
        message.Attempts.ShouldBe(1);
        message.Error.ShouldNotBeNull().ShouldContain("temporary SMTP failure");

        await processor.ProcessAsync();

        message.ProcessedOnUtc.ShouldNotBeNull();
        message.Error.ShouldBeNull();
        sender.Delivered.ShouldHaveSingleItem().ToEmail.ShouldBe("owner@example.test");
        var attachment = sender.Delivered.Single().Attachments.ShouldHaveSingleItem();
        attachment.Content.ShouldBe(originalPdf);
        attachment.ContentType.ShouldBe("application/pdf");
        attachment.FileName.ShouldBe("WO-submitted.pdf");
        await processor.ProcessAsync();
        sender.Delivered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Previously_queued_invitation_without_attachment_field_is_still_delivered()
    {
        var protector = new DataProtectionEmailContentProtector(new EphemeralDataProtectionProvider());
        var legacyJson = JsonSerializer.Serialize(new
        {
            ToEmail = "invited@example.test",
            ToName = "Invited employee",
            Subject = "Activate your account",
            ProtectedBody = protector.Protect("<p>Activation link</p>"),
            Kind = "invitation"
        });
        var legacy = JsonSerializer.Deserialize<EmailDeliveryRequested>(legacyJson)!;
        var sender = new RetrySender(failFirst: false);

        await new EmailDeliveryRequestedHandler(sender, protector).HandleAsync(legacy);

        sender.Delivered.ShouldHaveSingleItem().HtmlBody.ShouldBe("<p>Activation link</p>");
        sender.Delivered.Single().Attachments.ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_protected_attachment_payload_fails_before_sending_for_outbox_retry()
    {
        var protector = new DataProtectionEmailContentProtector(new EphemeralDataProtectionProvider());
        var sender = new RetrySender(failFirst: false);
        var request = new EmailDeliveryRequested
        {
            ToEmail = "owner@example.test", ToName = "Owner", Subject = "Submitted",
            ProtectedBody = protector.Protect("<p>Submitted</p>"),
            ProtectedAttachments = protector.Protect("invalid attachment json")
        };

        await Should.ThrowAsync<JsonException>(() => new EmailDeliveryRequestedHandler(sender, protector).HandleAsync(request));

        sender.Delivered.ShouldBeEmpty();
    }

    private static OperationsDbContext CreateDb() => new(new DbContextOptionsBuilder<OperationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class EmailDispatcher(EmailDeliveryRequestedHandler handler) : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default) =>
            handler.HandleAsync((EmailDeliveryRequested)integrationEvent, cancellationToken);
    }

    private sealed class RetrySender(bool failFirst = true) : IEmailSender
    {
        private bool shouldFail = failFirst;
        public List<EmailMessage> Delivered { get; } = [];
        public bool IsEnabled => true;
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (shouldFail)
            {
                shouldFail = false;
                throw new IOException("temporary SMTP failure");
            }
            Delivered.Add(message);
            return Task.CompletedTask;
        }
    }
}
