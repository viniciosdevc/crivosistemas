using Crivosistemas.Domain.Audit;
using Crivosistemas.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crivosistemas.Infrastructure.Persistence.Configurations;

internal static class MoneyColumn
{
    public static PropertyBuilder<decimal> IsMoney(this PropertyBuilder<decimal> p) => p.HasPrecision(12, 2);
    public static PropertyBuilder<decimal?> IsMoney(this PropertyBuilder<decimal?> p) => p.HasPrecision(12, 2);
}

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.PasswordHash).HasMaxLength(200);
        b.Ignore(x => x.IsAdmin);
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => x.Email).IsUnique();
    }
}

internal sealed class UserTokenConfig : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Purpose });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasskeyConfig : IEntityTypeConfiguration<Passkey>
{
    public void Configure(EntityTypeBuilder<Passkey> b)
    {
        b.Property(x => x.CredentialId).HasMaxLength(1024);
        b.Property(x => x.PublicKey).HasMaxLength(1024);
        b.Property(x => x.Name).HasMaxLength(80);
        b.HasIndex(x => x.CredentialId).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditEntryConfig : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.Property(x => x.Action).HasMaxLength(60);
        b.Property(x => x.EntityType).HasMaxLength(60);
        b.Property(x => x.Data).HasColumnType("json");
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.CreatedAt);
    }
}
