using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Opportunities;

internal sealed class SuccessManagementCriterionConfiguration
    : IEntityTypeConfiguration<SuccessManagementCriterion>
{
    public void Configure(EntityTypeBuilder<SuccessManagementCriterion> builder)
    {
        builder.ToTable("success_management_criteria");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Output).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SuccessCriteria).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(1000);

        builder.HasOne<Opportunity>()
            .WithMany()
            .HasForeignKey(x => x.OpportunityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OpportunityId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_success_management_criteria_opportunity");
    }
}
