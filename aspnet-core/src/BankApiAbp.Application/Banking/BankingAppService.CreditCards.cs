using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Cards;
using BankApiAbp.Transactions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    public async Task<IdResponseDto> CreateCreditCardAsync(CreateCreditCardDto input)
    {
        var userId = CurrentUserIdOrThrow();

        var cardNo = NormalizeCardNo(input.CardNo);
        _ = await GetCustomerOwnedAsync(input.CustomerId);

        var creditCardsQ = await _creditCards.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var cardExistsForUser = await AsyncExecuter.AnyAsync(
            from cc in creditCardsQ
            join c in customersQ on cc.CustomerId equals c.Id
            where c.UserId == userId && cc.CardNo == cardNo
            select cc.Id
        );

        if (cardExistsForUser)
            throw new UserFriendlyException("Bu kredi kart numarası zaten mevcut.");

        var card = new CreditCard(
            GuidGenerator.Create(),
            input.CustomerId,
            cardNo,
            input.ExpireAt,
            input.Cvv,
            input.Limit
        );

        await _creditCards.InsertAsync(card, autoSave: true);
        return new IdResponseDto { Id = card.Id };
    }

    public async Task CreditCardSpendAsync(CardSpendDto input)
    {
        var cardNo = NormalizeCardNo(input.CardNo);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var card = await GetCreditCardOwnedByCardNoAsync(cardNo);

                var now = Clock.Now;
                card.EnsureUsable(now);
                card.VerifyCvv(input.Cvv);

                card.Spend(input.Amount);

                await _creditCards.UpdateAsync(card, autoSave: true);

                await _tx.InsertAsync(new Transaction(
                    GuidGenerator.Create(),
                    TransactionType.CreditCardSpend,
                    input.Amount,
                    input.Description,
                    null,
                    null,
                    card.Id
                ), autoSave: true);

                return;
            }
            catch (Exception ex) when (IsConcurrency(ex))
            {
                if (attempt == 3) throw ConcurrencyFriendly();
                await SmallBackoffAsync(attempt);
            }
        }
    }

    public async Task CreditCardPayAsync(CreditCardPayDto input)
    {
        if (input.Amount <= 0)
            throw new BusinessException("Amount must be greater than zero");

        var cardNo = NormalizeCardNo(input.CardNo);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var card = await GetCreditCardOwnedByCardNoAsync(cardNo);
                var now = Clock.Now;

                card.EnsureUsable(now);
                card.VerifyCvv(input.Cvv);

                var account = await GetAccountOwnedAsync(input.AccountId);

                if (account.Balance < input.Amount)
                    throw new BusinessException("InsufficientBalance")
                        .WithData("Balance", account.Balance)
                        .WithData("Amount", input.Amount);

                if (card.CurrentDebt < input.Amount)
                    throw new BusinessException("PaymentExceedsDebt")
                        .WithData("CurrentDebt", card.CurrentDebt)
                        .WithData("Amount", input.Amount);

                account.Withdraw(input.Amount);
                card.Pay(input.Amount);

                await _accounts.UpdateAsync(account, autoSave: true);
                await _creditCards.UpdateAsync(card, autoSave: true);

                await _tx.InsertAsync(new Transaction(
                    GuidGenerator.Create(),
                    TransactionType.CreditCardPayment,
                    input.Amount,
                    input.Description,
                    account.Id,
                    null,
                    card.Id
                ), autoSave: true);

                return;
            }
            catch (Exception ex) when (IsConcurrency(ex))
            {
                if (attempt == 3) throw ConcurrencyFriendly();
                await SmallBackoffAsync(attempt);
            }
        }
    }

    public async Task<CreditCardDto> GetCreditCardDto(string cardNo)
    {
        cardNo = NormalizeCardNo(cardNo);

        var card = await GetCreditCardOwnedByCardNoAsync(cardNo);

        return new CreditCardDto
        {
            Id = card.Id,
            CustomerId = card.CustomerId,
            CardNo = card.CardNo,
            ExpireAt = card.ExpireAt,
            Limit = card.Limit,
            CurrentDebt = card.CurrentDebt,
            IsActive = card.IsActive
        };
    }

    public async Task<CardSpendSummaryDto> GetCreditCardSpendSummaryAsync(string cardNo)
    {
        cardNo = NormalizeCardNo(cardNo);
        var card = await GetCreditCardOwnedByCardNoAsync(cardNo);

        var now = Clock.Now;
        var todayStart = now.Date;
        var tomorrow = todayStart.AddDays(1);

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var txQ = await _tx.GetQueryableAsync();

        var q = txQ.Where(t => t.CreditCardId == card.Id && t.TxType == TransactionType.CreditCardSpend);

        var todaySpend = await AsyncExecuter.SumAsync(
            q.Where(t => t.CreationTime >= todayStart && t.CreationTime < tomorrow),
            t => (decimal?)t.Amount) ?? 0m;

        var monthSpend = await AsyncExecuter.SumAsync(
            q.Where(t => t.CreationTime >= monthStart && t.CreationTime < nextMonth),
            t => (decimal?)t.Amount) ?? 0m;

        return new CardSpendSummaryDto
        {
            CardNo = card.CardNo,
            Today = todayStart,
            TodaySpend = todaySpend,
            MonthStart = monthStart,
            MonthSpend = monthSpend
        };
    }

    public async Task<PagedResultDto<CreditCardListItemDto>> GetMyCreditCardsAsync(MyCreditCardsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var creditCardsQ = await _creditCards.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var q =
            from cc in creditCardsQ
            join c in customersQ on cc.CustomerId equals c.Id
            where c.UserId == userId
            select cc;

        if (input.CustomerId.HasValue)
            q = q.Where(cc => cc.CustomerId == input.CustomerId.Value);

        if (!string.IsNullOrWhiteSpace(input.CardNo))
        {
            var n = NormalizeCardNo(input.CardNo);
            q = q.Where(cc => cc.CardNo == n);
        }

        var total = await AsyncExecuter.CountAsync(q);

        q = q.OrderBy(x => x.CardNo);

        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<CreditCardListItemDto>(
            total,
            items.Select(cc => new CreditCardListItemDto
            {
                Id = cc.Id,
                CustomerId = cc.CustomerId,
                CardNo = cc.CardNo,
                ExpireAt = cc.ExpireAt,
                Limit = cc.Limit,
                CurrentDebt = cc.CurrentDebt,
                IsActive = cc.IsActive
            }).ToList()
        );
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
}
