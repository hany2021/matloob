using Matloob.Domain.Establishments;
using Matloob.Domain.Events;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Events;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).IsRequired();
        builder.Property(x => x.LocationTitle).HasMaxLength(255);
        builder.Property(x => x.Size).HasMaxLength(32);
        builder.Property(x => x.Classification).HasMaxLength(64);

        builder.Property(x => x.Latitude).HasColumnType("numeric(8,6)");
        builder.Property(x => x.Longitude).HasColumnType("numeric(9,6)");

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(x => x.EstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EventType>()
            .WithMany()
            .HasForeignKey(x => x.EventTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(x => x.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<City>()
            .WithMany()
            .HasForeignKey(x => x.CityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.EstablishmentId, x.Status })
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_events_establishment_status");
    }
}
