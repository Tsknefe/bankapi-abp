using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Transactions;

public class Transaction : AuditedAggregateRoot<Guid>
{
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }

    public Guid? AccountId { get; private set; }
    public Guid? DebitCardId { get; private set; }
    public Guid? CreditCardId { get; private set; }

    private Transaction() { }

    public Transaction(
        Guid id,
        TransactionType type,
        decimal amount,
        string? description,
        Guid? accountId,
        Guid? debitCardId,
        Guid? creditCardId) : base(id)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.");

        var filled = 0;
        if (accountId.HasValue) filled++;
        if (debitCardId.HasValue) filled++;
        if (creditCardId.HasValue) filled++;
        if (filled != 1) throw new ArgumentException("Exactly one source must be set.");

        Type = type;
        Amount = amount;
        Description = description;

        AccountId = accountId;
        DebitCardId = debitCardId;
        CreditCardId = creditCardId;
    }
}
