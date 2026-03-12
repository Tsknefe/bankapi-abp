using System;
using BankApiAbp.Banking;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Entities;

public class LedgerEntry : FullAuditedAggregateRoot<Guid>
{
    public Guid TransactionId { get; private set; }

    public Guid AccountId { get; private set; }

    public LedgerDirection Direction { get; private set; }

    public decimal Amount { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public string? Description { get; private set; } = default!;

    private LedgerEntry()
    {
    }

    public LedgerEntry(
    Guid id,
    Guid transactionId,
    Guid accountId,
    LedgerDirection direction,
    decimal amount,
    decimal balanceAfter,
    string? description)
    : base(id)
    {
        TransactionId = transactionId;
        AccountId = accountId;
        Direction = direction;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Description = description;
    }
}