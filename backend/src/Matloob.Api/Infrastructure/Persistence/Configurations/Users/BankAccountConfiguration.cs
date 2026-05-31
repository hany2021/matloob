using Matloob.Domain.Reference;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("bank_accounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Iban).HasMaxLength(34).IsRequired();

        builder.HasOne<Bank>()
            .WithMany()
            .HasForeignKey(x => x.BankId)
            .OnDelete(DeleteBehavior.Restrict);

        // One active bank account per user.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_bank_accounts_user_active");
    }
}
