using BankApiAbp.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class BankingIdempotencyRecordConfiguration : IEntityTypeConfiguration<BankingIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<BankingIdempotencyRecord> b)
    {
        b.ToTable("BankingIdempotencyRecords");
        b.HasKey(x => x.Id);

        b.Property(x => x.Operation).IsRequired().HasMaxLength(128);
        b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        b.Property(x => x.Status).IsRequired().HasMaxLength(32);
        b.Property(x => x.RequestHash).HasMaxLength(128);

        b.HasIndex(x => new { x.UserId, x.Operation, x.IdempotencyKey })
         .IsUnique();
    }
}