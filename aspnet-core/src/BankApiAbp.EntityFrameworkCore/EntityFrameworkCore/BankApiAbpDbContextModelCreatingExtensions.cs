using BankApiAbp.Banking;
using BankApiAbp.Banking.Messaging;
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

    }

    public static void ConfigureBanking(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Account>(b =>
        {
            b.ToTable("Accounts");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.Property(x => x.Iban).IsRequired().HasMaxLength(34);
            b.Property(x => x.Balance).HasColumnType("numeric(18,2)");
            b.Property(x => x.RowVersion)
                .IsRowVersion()
                .IsRequired(false);

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
            b.Property(x => x.RowVersion)
                .IsRowVersion()
                .IsRequired(false);

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
            b.Property(x => x.RowVersion)
                .IsRowVersion()
                .IsRequired(false);

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

        builder.Entity<BankingIdempotencyRecord>(b =>
        {
            b.ToTable("BankingIdempotencyRecords");
            b.ConfigureByConvention();

            b.HasIndex(x => new { x.UserId, x.Operation, x.IdempotencyKey }).IsUnique();

            b.Property(x => x.Operation).IsRequired().HasMaxLength(128);
            b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.RequestHash).HasMaxLength(256);
            b.Property(x => x.Status).IsRequired().HasMaxLength(32);
        });

        builder.Entity<InboxMessage>(b =>
        {
            b.ToTable("BankingInboxMessages");

            b.ConfigureByConvention();

            b.Property(x => x.EventId)
                .IsRequired();

            b.Property(x => x.EventName)
                .IsRequired()
                .HasMaxLength(256);

            b.Property(x => x.ConsumerName)
                .IsRequired()
                .HasMaxLength(256);

            b.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(64);

            b.Property(x => x.PayloadHash)
                .HasMaxLength(128);

            b.Property(x => x.Error)
                .HasMaxLength(4000);

            b.Property(x => x.RetryCount)
                .IsRequired();

            b.HasIndex(x => new { x.ConsumerName, x.EventId })
                .IsUnique();

            b.HasIndex(x => x.Status);

            b.HasIndex(x => x.ProcessedAt);

            b.HasIndex(x => x.LastAttemptTime);
        });
        builder.Entity<TransferAuditLog>(b =>
        {
            b.ToTable("BankingTransferAuditLogs");

            b.ConfigureByConvention();

            b.Property(x => x.EventId).IsRequired();
            b.Property(x => x.TransferId).IsRequired();
            b.Property(x => x.FromAccountId).IsRequired();
            b.Property(x => x.ToAccountId).IsRequired();
            b.Property(x => x.UserId).IsRequired();

            b.Property(x => x.Amount)
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            b.Property(x => x.Description)
                .HasMaxLength(512);

            b.Property(x => x.IdempotencyKey)
                .HasMaxLength(128);

            b.Property(x => x.EventName)
                .HasMaxLength(256)
                .IsRequired();

            b.Property(x => x.OccurredAtUtc)
                .IsRequired();

            b.HasIndex(x => x.EventId).IsUnique();
            b.HasIndex(x => x.TransferId);
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => x.CreationTime);
        });

        builder.Entity<TransferNotificationLog>(b =>
        {
            b.ToTable("BankingTransferNotifications");

            b.ConfigureByConvention();

            b.Property(x => x.EventId).IsRequired();
            b.Property(x => x.TransferId).IsRequired();
            b.Property(x => x.UserId).IsRequired();
            b.Property(x => x.FromAccountId).IsRequired();
            b.Property(x => x.ToAccountId).IsRequired();

            b.Property(x => x.Amount)
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            b.Property(x => x.Description)
                .HasMaxLength(512);

            b.Property(x => x.IdempotencyKey)
                .HasMaxLength(128);

            b.Property(x => x.Channel)
                .HasMaxLength(64)
                .IsRequired();

            b.Property(x => x.Status)
                .HasMaxLength(64)
                .IsRequired();

            b.Property(x => x.EventName)
                .HasMaxLength(256)
                .IsRequired();

            b.Property(x => x.OccurredAtUtc)
                .IsRequired();

            b.HasIndex(x => x.EventId);
            b.HasIndex(x => x.TransferId);
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => x.CreationTime);
        });
    }
}
