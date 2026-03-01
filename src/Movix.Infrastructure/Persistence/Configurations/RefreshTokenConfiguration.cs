using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movix.Domain.Entities;

namespace Movix.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(500).IsRequired();
        b.Property(x => x.ReplacedByTokenId).HasMaxLength(50);
        b.Property(x => x.RevokedByIp).HasMaxLength(50);
        b.Property(x => x.RevocationReason).HasMaxLength(100);
        b.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("UpdatedAtUtc");
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
    }
}
