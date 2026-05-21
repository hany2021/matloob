using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

/// <summary>
/// EF mapping for <see cref="User"/>. snake_case names come from the
/// global naming convention.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IdentityId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // Partial unique on IdentityId across non-soft-deleted rows. The
        // sync service's lookup (by sub claim) is the hot path.
        builder.HasIndex(x => x.IdentityId)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_users_identity_id_active");

        builder.HasIndex(x => x.IsActive).HasFilter("is_deleted = false");
    }
}
