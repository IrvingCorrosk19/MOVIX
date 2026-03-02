using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.CreatedAtUtc);
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.IsActive);
    }
}
