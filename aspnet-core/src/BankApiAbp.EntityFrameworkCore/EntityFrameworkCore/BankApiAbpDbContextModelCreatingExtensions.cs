using BankApiAbp.Cards;
using BankApiAbp.Entities;
using BankApiAbp.Transactions;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace BankApiAbp.EntityFrameworkCore;

public static class BankApiAbpDbContextModelCreatingExtensions
{
    public static void ConfigureBankApiAbp(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        // İstersen burada "prefix/schema" gibi sabitleri de kullanırsın.
        // Şimdilik boş kalsın; Banking ayrı extension'da.
    }

    public static void ConfigureBanking(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.Property(x => x.TcNo).IsRequired().HasMaxLength(11);
            b.Property(x => x.BirthPlace).IsRequired().HasMaxLength(50);

            b.HasIndex(x => x.TcNo).IsUnique();
        });

        builder.Entity<Account>(b =>
        {
            b.ToTable("Accounts");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.Property(x => x.Iban).IsRequired().HasMaxLength(34);
            b.Property(x => x.Balance).HasColumnType("numeric(18,2)");
            b.Property(x => x.RowVersion).IsRowVersion();

            b.HasIndex(x => x.Iban).IsUnique();

            b.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DebitCard>(b =>
        {
            b.ToTable("DebitCards");
            b.ConfigureByConvention();

            b.Property(x => x.CardNo).IsRequired().HasMaxLength(16);
            b.Property(x => x.CvvHash).IsRequired().HasMaxLength(500);
            b.Property(x => x.RowVersion).IsRowVersion();


            b.HasIndex(x => x.CardNo).IsUnique();

            b.HasOne<Account>()
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CreditCard>(b =>
        {
            b.ToTable("CreditCards");
            b.ConfigureByConvention();

            b.Property(x => x.CardNo).IsRequired().HasMaxLength(16);
            b.Property(x => x.CvvHash)
                .HasMaxLength(500)
                .IsRequired();

            b.Property(x => x.Limit).HasColumnType("numeric(18,2)");
            b.Property(x => x.CurrentDebt).HasColumnType("numeric(18,2)");
            b.Property(x => x.RowVersion).IsRowVersion();

            b.HasIndex(x => x.CardNo).IsUnique();

            b.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Transaction>(b =>
        {
            b.ToTable("Transactions");
            b.ConfigureByConvention();

            b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            b.Property(x => x.Description).HasMaxLength(100);


            b.HasIndex(x => x.AccountId);
            b.HasIndex(x => x.DebitCardId);
            b.HasIndex(x => x.CreditCardId);

            b.HasCheckConstraint("CK_Transactions_Owner",
                "\"AccountId\" IS NOT NULL OR \"DebitCardId\" IS NOT NULL OR \"CreditCardId\" IS NOT NULL");
        });
    }
}
