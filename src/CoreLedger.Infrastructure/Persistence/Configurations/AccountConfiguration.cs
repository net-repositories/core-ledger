using CoreLedger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreLedger.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Balance).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Currency).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Оптимистичная блокировка через системную колонку xmin в PostgreSQL
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.User)
            .WithMany(u => u.Accounts)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
