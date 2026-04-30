using System;

namespace BankApiAbp.Banking.Dtos;

public class TransferResultDto
{
    public Guid TransactionOutId { get; set; }
    public Guid TransactionInId { get; set; }

    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }

    public decimal Amount { get; set; }

    public decimal FromNewBalance { get; set; }
    public decimal ToNewBalance { get; set; }

    public string IdempotencyKey { get; set; } = default!;
    public DateTime ProcessedAtUtc { get; set; }
}