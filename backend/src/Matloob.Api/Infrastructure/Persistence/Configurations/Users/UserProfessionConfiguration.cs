using Matloob.Domain.Reference;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

internal sealed class UserProfessionConfiguration : IEntityTypeConfiguration<UserProfession>
{
    public void Configure(EntityTypeBuilder<UserProfession> builder)
    {
        builder.ToTable("user_professions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Other).HasMaxLength(255);

        builder.HasOne<OpportunityCategory>()
            .WithMany()
            .HasForeignKey(x => x.OpportunityCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId).HasFilter("is_deleted = false");

        builder.HasIndex(x => new { x.UserId, x.OpportunityCategoryId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_user_professions_user_category_active");
    }
}
