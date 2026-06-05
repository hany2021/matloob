using Matloob.Domain.Establishments;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Opportunities;

/// <summary>
/// EF mapping for <see cref="Opportunity"/>. snake_case names from the
/// global naming convention; soft-delete query filter applied centrally
/// in AppDbContext.
///
/// <para>
/// FK choices:
/// - issuer_establishment_id: hard FK to establishments.id, Restrict.
/// - opportunity_category_id: hard FK to opportunity_categories.id, Restrict.
/// - city_id / nationality_id: hard FK to the reference lookups, SetNull on delete.
/// - event_id: bare Guid for now — the Event entity does not exist in the
///   new system yet. FK is wired when the Event slice lands.
/// </para>
/// </summary>
internal sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("opportunities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Required text fields.
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(3000).IsRequired();
        builder.Property(x => x.LocationTitle).HasMaxLength(200).IsRequired();

        // Geo precision matches the legacy schema and the establishment
        // table conventions.
        builder.Property(x => x.Latitude).HasColumnType("numeric(8,6)").IsRequired();
        builder.Property(x => x.Longitude).HasColumnType("numeric(9,6)").IsRequired();

        // Monetary fields.
        builder.Property(x => x.MonthlySalary).HasColumnType("numeric(10,2)");
        builder.Property(x => x.Fees).HasColumnType("numeric(10,2)");

        // Contact info.
        builder.Property(x => x.PhoneContactInformation).HasMaxLength(30);
        builder.Property(x => x.EmailContactInformation).HasMaxLength(320);

        // Lifecycle.
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // Enum bitflag storage — kept as the underlying int so we can do
        // bitwise WHERE filters (e.g. `genders & 1 = 1` for male-allowed).
        builder.Property(x => x.EstablishmentClassifications)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(x => x.Genders)
            .HasConversion<int>()
            .IsRequired();

        // WorkingHoursType is nullable — store the legacy wire token
        // (full_time/part_time), NOT the C# member name, so the column matches
        // the legacy DB and round-trips with the frontend.
        builder.Property(x => x.WorkingHoursType)
            .HasConversion(
                v => v.HasValue ? v.Value.ToWire() : null,
                s => WorkingHoursTypeWire.Parse(s))
            .HasMaxLength(32);

        builder.Property(x => x.EndedByUserId).HasMaxLength(200);

        // --- Relationships ---------------------------------------------------
        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.IssuerEstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OpportunityCategory>()
            .WithMany()
            .HasForeignKey(x => x.OpportunityCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<City>()
            .WithMany()
            .HasForeignKey(x => x.CityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Nationality>()
            .WithMany()
            .HasForeignKey(x => x.NationalityId)
            .OnDelete(DeleteBehavior.SetNull);

        // TODO: when the Event entity lands, add:
        //   builder.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId)
        //          .OnDelete(DeleteBehavior.Restrict);
        // Until then event_id is just a Guid column with no FK.

        // --- Indexes --------------------------------------------------------
        builder.HasIndex(x => new { x.IssuerEstablishmentId, x.Status })
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_opportunities_issuer_status");

        builder.HasIndex(x => x.OpportunityCategoryId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_opportunities_category");

        builder.HasIndex(x => x.EventId)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_opportunities_event");

        builder.HasIndex(x => x.Status)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_opportunities_status");
    }
}
