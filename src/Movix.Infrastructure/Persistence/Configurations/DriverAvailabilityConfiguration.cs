using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class DriverAvailabilityConfiguration : IEntityTypeConfiguration<DriverAvailability>
{
    public void Configure(EntityTypeBuilder<DriverAvailability> b)
    {
        b.ToTable("driver_availability");
        b.HasKey(x => x.DriverId);
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Driver).WithOne().HasForeignKey<DriverAvailability>(x => x.DriverId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.IsOnline, x.CurrentTripId });
    }
}
