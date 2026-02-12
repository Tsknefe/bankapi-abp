using BankApiAbp.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankApiAbp.EntityFrameworkCore.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");

        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Iban).IsRequired().HasMaxLength(34);

        builder.HasIndex(x => x.Iban).IsUnique();

        builder.Property(x => x.Balance).HasPrecision(18, 2);
    }
}
