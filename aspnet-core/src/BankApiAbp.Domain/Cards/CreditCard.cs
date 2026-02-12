using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Cards;

public class CreditCard : FullAuditedAggregateRoot<Guid>
{
    public string CardNo { get; private set; } = null!;
    public DateTime ExpireAt { get; private set; }
    public string Cvv { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public decimal Limit { get; private set; }
    public decimal CurrentDebt { get; private set; }

    public Guid CustomerId { get; private set; }

    private CreditCard() { }

    public CreditCard(
        Guid id,
        Guid customerId,
        string cardNo,
        DateTime expireAt,
        string cvv,
        decimal limit) : base(id)
    {
        if (limit <= 0) throw new ArgumentException("Limit must be > 0.");

        CustomerId = customerId;
        SetCardNo(cardNo);
        ExpireAt = expireAt;
        SetCvv(cvv);

        Limit = limit;
        CurrentDebt = 0;
        IsActive = true;
    }

    public void SetCardNo(string cardNo)
    {
        if (string.IsNullOrWhiteSpace(cardNo)) throw new ArgumentException("CardNo required.");
        CardNo = cardNo.Trim();
    }

    public void SetCvv(string cvv)
    {
        if (string.IsNullOrWhiteSpace(cvv)) throw new ArgumentException("CVV required.");
        Cvv = cvv.Trim();
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public void EnsureUsable()
    {
        if (!IsActive) throw new InvalidOperationException("Credit card is not active.");
        if (ExpireAt.Date < DateTime.UtcNow.Date) throw new InvalidOperationException("Credit card expired.");
    }

    public void Spend(decimal amount)
    {
        EnsureUsable();
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.");
        if (CurrentDebt + amount > Limit) throw new InvalidOperationException("Limit exceeded.");
        CurrentDebt += amount;
    }

    public void PayDebt(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.");
        CurrentDebt = Math.Max(0, CurrentDebt - amount);
    }
}
