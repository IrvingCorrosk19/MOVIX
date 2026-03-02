using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.ToTable("trips");
        b.HasKey(x => x.Id);
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PickupAddress).HasMaxLength(500);
        b.Property(x => x.DropoffAddress).HasMaxLength(500);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.DistanceKm).HasPrecision(18, 4);
        b.Property(x => x.DurationMinutes).HasPrecision(18, 4);
        b.Property(x => x.BaseFareUsed).HasPrecision(18, 4);
        b.Property(x => x.PricePerKmUsed).HasPrecision(18, 4);
        b.Property(x => x.PricePerMinuteUsed).HasPrecision(18, 4);
        b.Property(x => x.MinimumFareUsed).HasPrecision(18, 4);
        b.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("UpdatedAtUtc");
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => x.PassengerId);
        b.HasIndex(x => x.DriverId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.CreatedAtUtc);
        b.HasIndex(x => x.TenantId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).IsRequired(false);
        b.HasIndex(x => x.PickupLocation).HasMethod("GIST");
        b.HasIndex(x => x.DropoffLocation).HasMethod("GIST");
    }
}
