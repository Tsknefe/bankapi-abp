using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Cards;

public class CreditCard : FullAuditedAggregateRoot<Guid>
{
    public Guid CustomerId { get; private set; }
    public string CardNo { get; private set; } = default!;
    public DateTime ExpireAt { get; private set; }
    public string CvvHash { get; private set; } = default!;
    public decimal Limit { get; private set; }
    public decimal CurrentDebt { get; private set; }
    public bool IsActive { get; private set; } = true;

    [Timestamp]
    public byte[] RowVersion { get; private set; } = default!;

    private CreditCard() { }

    public CreditCard(Guid id, Guid customerId, string cardNo, DateTime expireAt, string cvv, decimal limit)
        : base(id)
    {
        CustomerId = customerId;
        CardNo = cardNo;
        ExpireAt = expireAt;
        CvvHash = BCrypt.Net.BCrypt.HashPassword(cvv);
        Limit = limit;
        CurrentDebt = 0;
        IsActive = true;
    }

    public void Spend(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0");
        if (!IsActive) throw new InvalidOperationException("Card is not active");
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

    public void EnsureUsable(DateTime now)
    {
        if (!IsActive) throw new BusinessException("CreditCardNotActive");
        if (ExpireAt < now) throw new BusinessException("CreditCardExpired");
    }

    public void VerifyCvv(string cvv)
    {
        if (string.IsNullOrWhiteSpace(cvv)) throw new BusinessException("CvvRequired");
        if (cvv.Length < 3 || cvv.Length > 4) throw new BusinessException("CvvInvalid");
        if (!BCrypt.Net.BCrypt.Verify(cvv, CvvHash))
            throw new BusinessException("CreditCardInvalidCvv");
    }
    public void SetCvv(string cvv)
    {
        if (string.IsNullOrWhiteSpace(cvv)) throw new BusinessException("Cvv Required");
        if (cvv.Length < 3 || cvv.Length > 4) throw new BusinessException("Cvv Invalid");

        CvvHash = BCrypt.Net.BCrypt.HashPassword(cvv);
    }

}
