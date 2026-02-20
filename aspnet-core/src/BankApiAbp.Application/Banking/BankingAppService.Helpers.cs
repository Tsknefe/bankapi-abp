using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Entities;
using BankApiAbp.Cards;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    private static string NormalizeCardNo(string? cardNo)
    {
        cardNo = (cardNo ?? "").Trim();
        if (cardNo.Length != 16 || !cardNo.All(char.IsDigit))
            throw new UserFriendlyException("CardNo 16 haneli ve sadece rakamlardan oluşmalı.");
        return cardNo;
    }

    private Guid CurrentUserIdOrThrow()
    {
        if (!CurrentUser.IsAuthenticated)
            throw new AbpAuthorizationException("Not authenticated.");
        return CurrentUser.GetId();
    }

    private async Task<Customer> GetCustomerOwnedAsync(Guid customerId)
    {
        var userId = CurrentUserIdOrThrow();

        var cust = await _customers.FindAsync(customerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu müşteriye erişimin yok.");

        return cust;
    }

    private async Task<Account> GetAccountOwnedAsync(Guid accountId)
    {
        var userId = CurrentUserIdOrThrow();

        var acc = await _accounts.FindAsync(accountId);
        if (acc == null) throw new UserFriendlyException("Hesap bulunamadı.");

        var cust = await _customers.FindAsync(acc.CustomerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu hesaba erişimin yok.");

        return acc;
    }

    private async Task<DebitCard> GetDebitCardOwnedByCardNoAsync(string cardNo)
    {
        var userId = CurrentUserIdOrThrow();

        var card = await _debitCards.FirstOrDefaultAsync(x => x.CardNo == cardNo);
        if (card == null) throw new UserFriendlyException("Debit card not found.");

        var acc = await _accounts.FindAsync(card.AccountId);
        if (acc == null) throw new UserFriendlyException("Hesap bulunamadı.");

        var cust = await _customers.FindAsync(acc.CustomerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu karta erişimin yok.");

        return card;
    }

    private async Task<CreditCard> GetCreditCardOwnedByCardNoAsync(string cardNo)
    {
        var userId = CurrentUserIdOrThrow();

        var card = await _creditCards.FirstOrDefaultAsync(x => x.CardNo == cardNo);
        if (card == null) throw new UserFriendlyException("Credit card not found.");

        var cust = await _customers.FindAsync(card.CustomerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu kredi kartına erişimin yok.");

        return card;
    }

    private static UserFriendlyException ConcurrencyFriendly()
        => new UserFriendlyException("İşlem aynı anda başka bir istekle çakıştı. Lütfen tekrar deneyin.");

    private static bool IsConcurrency(Exception ex)
        => ex is Volo.Abp.Data.AbpDbConcurrencyException;

    private static Task SmallBackoffAsync(int attempt)
    {
        var ms = attempt switch { 1 => 20, 2 => 40, _ => 80 };
        return Task.Delay(ms);
    }
}
