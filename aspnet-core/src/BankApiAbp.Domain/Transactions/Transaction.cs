using System;
using BankApiAbp.Banking;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Transactions;

public class Transaction : FullAuditedAggregateRoot<Guid>
{
    public TransactionType TxType { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }

    public Guid? AccountId { get; private set; }
    public Guid? DebitCardId { get; private set; }
    public Guid? CreditCardId { get; private set; }

    private Transaction() { }

    public Transaction(Guid id, TransactionType txType, decimal amount, string? description,
        Guid? accountId = null, Guid? debitCardId = null, Guid? creditCardId = null)
        : base(id)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0");

        TxType = txType;
        Amount = amount;
        Description = description;

        AccountId = accountId;
        DebitCardId = debitCardId;
        CreditCardId = creditCardId;

        if (AccountId == null && DebitCardId == null && CreditCardId == null)
            throw new ArgumentException("At least one owner must be set (Account/DebitCard/CreditCard).");
    }
}
