using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Cards;

public class DebitCard : FullAuditedAggregateRoot<Guid>
{
    public string CardNo { get; private set; } = null!;
    public DateTime ExpireAt { get; private set; }
    public string Cvv { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public Guid AccountId { get; private set; }

    private DebitCard() { }

    public DebitCard(Guid id, Guid accountId, string cardNo, DateTime expireAt, string cvv)
        : base(id)
    {
        AccountId = accountId;
        SetCardNo(cardNo);
        ExpireAt = expireAt;
        SetCvv(cvv);
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
        if (!IsActive) throw new InvalidOperationException("Debit card is not active.");
        if (ExpireAt.Date < DateTime.UtcNow.Date) throw new InvalidOperationException("Debit card expired.");
    }
}
