using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.ToTable("vehicles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Plate).HasMaxLength(20).IsRequired();
        b.Property(x => x.Model).HasMaxLength(100).IsRequired();
        b.Property(x => x.Color).HasMaxLength(50).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("UpdatedAtUtc");
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.DriverId);
    }
}
