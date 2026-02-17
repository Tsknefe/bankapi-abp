using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Cards;

public class DebitCard : FullAuditedAggregateRoot<Guid>
{
    public Guid AccountId { get; private set; }
    public string CardNo { get; private set; } = default!;
    public DateTime ExpireAt { get; private set; }
    //public string Cvv { get; private set; } = default!;
    public string CvvHash { get; set; } = default!;
    public bool IsActive { get; private set; } = true;
    public decimal DailyLimit { get; set; } = 5000m;
    private DebitCard() { }

    public DebitCard(Guid id, Guid accountId, string cardNo, DateTime expireAt, string cvv, decimal dailyLimit = 5000m)
        : base(id)
    {
        AccountId = accountId;
        CardNo = cardNo;
        ExpireAt = expireAt;
        DailyLimit = dailyLimit;
        CvvHash = BCrypt.Net.BCrypt.HashPassword(cvv);
        //Cvv = cvv;
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void SetDailylimit(decimal dailyLimit)
    {
        if (dailyLimit <= 0)
        {
            throw new BusinessException("Daily Limit Invalıd ");
            DailyLimit = dailyLimit;
        }

    }
    public void SetCvv(string cvv)
    {
        if (string.IsNullOrWhiteSpace(cvv)) throw new BusinessException("Cvv required");
        if (cvv.Length < 3 || cvv.Length > 4) throw new BusinessException("Cvv Invalid");

        CvvHash=BCrypt.Net.BCrypt.HashPassword(cvv);
    }

    public void EnsureUsable(DateTime now)
    {
        if (!IsActive) throw new BusinessException("Debit Card Not Active");
        if (ExpireAt < now) throw new BusinessException("Debit Card Expired");

    }

    public void VerifyCvv(string cvv)
    {
        if (!BCrypt.Net.BCrypt.Verify(cvv, CvvHash)) throw new BusinessException("Debit Card Invalid Cvv");
    }
}
