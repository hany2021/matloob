using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Establishments;

internal sealed class EstablishmentExperienceConfiguration
    : IEntityTypeConfiguration<EstablishmentExperience>
{
    public void Configure(EntityTypeBuilder<EstablishmentExperience> builder)
    {
        builder.ToTable("establishment_experiences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Type)
            .HasConversion(v => v.ToWire(), v => ParseType(v))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.JobTitle).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).IsRequired();
        builder.Property(x => x.From).IsRequired();
        builder.Property(x => x.To).IsRequired();

        // category_id is polymorphic (event type or opportunity category by
        // Type) — no FK, the application validates existence on create.
        builder.Property(x => x.CategoryId).IsRequired();

        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.EstablishmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.EstablishmentId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_establishment_experiences_establishment");
    }

    private static EstablishmentExperienceType ParseType(string token) =>
        EstablishmentExperienceTypeWire.TryParse(token, out var t) ? t : EstablishmentExperienceType.Event;
}
