using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Reference;

internal sealed class OpportunityCategoryConfiguration : IEntityTypeConfiguration<OpportunityCategory>
{
    public void Configure(EntityTypeBuilder<OpportunityCategory> builder)
    {
        builder.ToTable("opportunity_categories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Icon).HasMaxLength(500);
        builder.Property(x => x.ForVacancy).HasDefaultValue(false);
        builder.Property(x => x.IsOther).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // Self-referencing parent / children. ParentId is nullable (roots).
        // Restrict on delete: removing a parent that still has children is a
        // bug; the admin should re-parent or deactivate first.
        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Backing field for the read-only Children collection.
        builder.Navigation(x => x.Children).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => new { x.ForVacancy, x.IsActive });
    }
}
