using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Cards;

public class DebitCard : FullAuditedAggregateRoot<Guid>
{
    public Guid AccountId { get; private set; }
    public string CardNo { get; private set; } = default!;
    public DateTime ExpireAt { get; private set; }
    public string CvvHash { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;
    public decimal DailyLimit { get; private set; } = 5000m;

    [Timestamp]
    public byte[] RowVersion { get; private set; } = default!;

    private DebitCard() { }

    public DebitCard(Guid id, Guid accountId, string cardNo, DateTime expireAt, string cvv, decimal dailyLimit = 5000m)
        : base(id)
    {
        AccountId = accountId;
        CardNo = cardNo;
        ExpireAt = expireAt;
        DailyLimit = dailyLimit;
        CvvHash = BCrypt.Net.BCrypt.HashPassword(cvv);
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public void SetDailyLimit(decimal dailyLimit)
    {
        if (dailyLimit <= 0)
            throw new BusinessException("DailyLimitInvalid");

        DailyLimit = dailyLimit;
    }

    public void SetCvv(string cvv)
    {
        if (string.IsNullOrWhiteSpace(cvv)) throw new BusinessException("CvvRequired");
        if (cvv.Length < 3 || cvv.Length > 4) throw new BusinessException("CvvInvalid");

        CvvHash = BCrypt.Net.BCrypt.HashPassword(cvv);
    }

    public void EnsureUsable(DateTime now)
    {
        if (!IsActive) throw new BusinessException("DebitCardNotActive");
        if (ExpireAt < now) throw new BusinessException("DebitCardExpired");
    }

    public void VerifyCvv(string cvv)
    {
        if (!BCrypt.Net.BCrypt.Verify(cvv, CvvHash))
            throw new BusinessException("DebitCardInvalidCvv");
    }
}
