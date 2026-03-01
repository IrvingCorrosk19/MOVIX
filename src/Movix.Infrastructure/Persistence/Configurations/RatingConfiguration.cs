using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> b)
    {
        b.ToTable("ratings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Comment).HasMaxLength(1000);
        b.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("UpdatedAtUtc");
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.HasIndex(x => x.TripId);
    }
}
