using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.EventId).IsRequired();
        b.HasIndex(x => x.EventId).IsUnique();
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.Property(x => x.Type).HasMaxLength(200).IsRequired();
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.Error).HasMaxLength(2000);
        b.Property(x => x.AttemptCount).HasDefaultValue(0);
        b.Property(x => x.LastAttemptUtc);
        b.Property(x => x.IsDeadLetter).HasDefaultValue(false);
        b.Property(x => x.DeadLetteredAtUtc);
        b.HasIndex(x => x.ProcessedAtUtc);
    }
}
