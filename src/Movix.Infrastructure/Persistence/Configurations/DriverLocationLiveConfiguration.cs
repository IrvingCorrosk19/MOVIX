using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class DriverLocationLiveConfiguration : IEntityTypeConfiguration<DriverLocationLive>
{
    public void Configure(EntityTypeBuilder<DriverLocationLive> b)
    {
        b.ToTable("driver_location_live");
        b.HasKey(x => x.Id);
        b.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("UpdatedAtUtc");
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.HasIndex(x => x.DriverId);
        b.HasIndex(x => x.RecordedAtUtc);
        b.HasIndex(x => x.Location).HasMethod("GIST");
    }
}
