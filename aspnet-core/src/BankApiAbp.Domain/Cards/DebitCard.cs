using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Cards;

public class DebitCard : FullAuditedAggregateRoot<Guid>
{
    public Guid AccountId { get; private set; }
    public string CardNo { get; private set; } = default!;
    public DateTime ExpireAt { get; private set; }
    public string Cvv { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    private DebitCard() { }

    public DebitCard(Guid id, Guid accountId, string cardNo, DateTime expireAt, string cvv)
        : base(id)
    {
        AccountId = accountId;
        CardNo = cardNo;
        ExpireAt = expireAt;
        Cvv = cvv;
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
