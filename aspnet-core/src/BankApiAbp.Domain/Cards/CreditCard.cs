using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Cards;

public class CreditCard : FullAuditedAggregateRoot<Guid>
{
    public Guid CustomerId { get; private set; }
    public string CardNo { get; private set; } = default!;
    public DateTime ExpireAt { get; private set; }
    public string Cvv { get; private set; } = default!;
    public decimal Limit { get; private set; }
    public decimal CurrentDebt { get; private set; }
    public bool IsActive { get; private set; } = true;

    private CreditCard() { }

    public CreditCard(Guid id, Guid customerId, string cardNo, DateTime expireAt, string cvv, decimal limit)
        : base(id)
    {
        CustomerId = customerId;
        CardNo = cardNo;
        ExpireAt = expireAt;
        Cvv = cvv;
        Limit = limit;
        CurrentDebt = 0;
        IsActive = true;
    }

    public void Spend(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0");
        if ((CurrentDebt + amount) > Limit) throw new InvalidOperationException("Limit exceeded");
        CurrentDebt += amount;
    }

    public void Pay(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0");
        if (amount > CurrentDebt) amount = CurrentDebt;
        CurrentDebt -= amount;
    }
    
    public void Deactivate() => IsActive = false;   
    public void Activate() => IsActive = true;
}
