using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ExternalPaymentId).HasMaxLength(200);
        b.Property(x => x.FailureReason).HasMaxLength(500);
        b.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("UpdatedAtUtc");
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.Property(x => x.RowVersion)
            .IsConcurrencyToken()
            .HasColumnType("bytea")
            .HasDefaultValueSql("gen_random_bytes(8)");
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => x.TripId);
    }
}
