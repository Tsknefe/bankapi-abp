using System;

namespace BankApiAbp.Banking.Dtos;

public class WithdrawResultDto
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public decimal NewBalance { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public DateTime ProcessedAtUtc { get; set; }
}