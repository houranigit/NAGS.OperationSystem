using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Messaging;

/// <summary>EF configuration for the shared outbox/inbox tables, applied per module schema.</summary>
public static class OutboxModelExtensions
{
    public static ModelBuilder ApplyOutboxInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Type).HasMaxLength(512).IsRequired();
            builder.Property(m => m.Content).IsRequired();
            builder.Property(m => m.OccurredOnUtc).IsRequired();
            builder.Property(m => m.Error).HasMaxLength(2048);
            builder.HasIndex(m => m.ProcessedOnUtc);
        });

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages");
            builder.HasKey(m => new { m.MessageId, m.Consumer });
            builder.Property(m => m.Consumer).HasMaxLength(256);
            builder.Property(m => m.ProcessedOnUtc).IsRequired();
        });

        return modelBuilder;
    }
}
