using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Mobile;

namespace Operations.Infrastructure.Persistence.Configurations;

internal sealed class MobileMutationConfiguration : IEntityTypeConfiguration<MobileMutation>
{
    public void Configure(EntityTypeBuilder<MobileMutation> builder)
    {
        builder.ToTable("Operations_MobileMutations");

        builder.HasKey(m => m.ClientMutationId);
        builder.Property(m => m.ClientMutationId).HasMaxLength(64);
        builder.Property(m => m.Kind).HasMaxLength(40).IsRequired();
        builder.Property(m => m.RequestFingerprint).HasMaxLength(64);

        builder.HasIndex(m => m.OwnerUserId);

        // One scratch flight per client-generated flight identity: retries and duplicate submissions
        // of the same offline ad-hoc flight collapse onto one server flight.
        builder.HasIndex(m => m.ClientFlightId)
            .IsUnique()
            .HasFilter("[ClientFlightId] IS NOT NULL");
    }
}
