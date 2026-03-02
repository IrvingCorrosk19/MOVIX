using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class TariffPlanConfiguration : IEntityTypeConfiguration<TariffPlan>
{
    public void Configure(EntityTypeBuilder<TariffPlan> b)
    {
        b.ToTable("tariff_plans");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.BaseFare).HasPrecision(18, 4);
        b.Property(x => x.PricePerKm).HasPrecision(18, 4);
        b.Property(x => x.PricePerMinute).HasPrecision(18, 4);
        b.Property(x => x.MinimumFare).HasPrecision(18, 4);
        b.Property(x => x.Priority).HasDefaultValue(100);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.IsActive, x.Priority, x.EffectiveFromUtc })
            .HasFilter("\"IsActive\" = true");
    }
}
