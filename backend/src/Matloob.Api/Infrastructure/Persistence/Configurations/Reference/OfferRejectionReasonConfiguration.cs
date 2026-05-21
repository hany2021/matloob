using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Reference;

internal sealed class OfferRejectionReasonConfiguration : IEntityTypeConfiguration<OfferRejectionReason>
{
    public void Configure(EntityTypeBuilder<OfferRejectionReason> builder)
    {
        builder.ToTable("offer_rejection_reasons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
        builder.Property(x => x.IsOther).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.IsActive).HasFilter("is_active = true");
    }
}
