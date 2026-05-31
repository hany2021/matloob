using Matloob.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.RecipientType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.RecipientId).HasMaxLength(128).IsRequired();

        builder.Property(x => x.Type).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Message).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(64);
        builder.Property(x => x.ResourceId).HasMaxLength(64);
        builder.Property(x => x.Image).HasMaxLength(1024);

        // Feed query: a recipient's notifications newest-first, with the unread
        // filter; matches the list + unread-count endpoints.
        builder.HasIndex(x => new { x.RecipientType, x.RecipientId, x.ReadAt })
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_notifications_recipient_read");
    }
}
