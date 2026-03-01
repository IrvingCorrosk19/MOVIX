using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class TripStatusHistoryConfiguration : IEntityTypeConfiguration<TripStatusHistory>
{
    public void Configure(EntityTypeBuilder<TripStatusHistory> b)
    {
        b.ToTable("trip_status_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("UpdatedAtUtc");
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.HasIndex(x => x.TripId);
    }
}
