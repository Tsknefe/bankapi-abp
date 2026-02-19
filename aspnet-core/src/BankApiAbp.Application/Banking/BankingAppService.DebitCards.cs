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
    public async Task<IdResponseDto> CreateDebitCardAsync(CreateDebitCardDto input)
    {
        var userId = CurrentUserIdOrThrow();

        var cardNo = NormalizeCardNo(input.CardNo);
        _ = await GetAccountOwnedAsync(input.AccountId);

        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var cardExistsForUser = await AsyncExecuter.AnyAsync(
            from dc in debitCardsQ
            join a in accountsQ on dc.AccountId equals a.Id
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId && dc.CardNo == cardNo
            select dc.Id
        );

        if (cardExistsForUser)
            throw new UserFriendlyException("Bu debit kart numarası zaten mevcut.");

        var card = new DebitCard(
            GuidGenerator.Create(),
            input.AccountId,
            cardNo,
            input.ExpireAt,
            input.Cvv
        );

        await _debitCards.InsertAsync(card, autoSave: true);
        return new IdResponseDto { Id = card.Id };
    }

    public async Task DebitCardSpendAsync(CardSpendDto input)
    {
        var cardNo = NormalizeCardNo(input.CardNo);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var card = await GetDebitCardOwnedByCardNoAsync(cardNo);

                var now = Clock.Now;
                card.EnsureUsable(now);
                card.VerifyCvv(input.Cvv);

                var account = await GetAccountOwnedAsync(card.AccountId);

                var start = now.Date;
                var end = start.AddDays(1);

                var txQ = await _tx.GetQueryableAsync();

                var spentToday = await AsyncExecuter.SumAsync(
                    txQ.Where(t => t.DebitCardId == card.Id
                                   && t.TxType == TransactionType.DebitCardSpend
                                   && t.CreationTime >= start
                                   && t.CreationTime < end),
                    t => (decimal?)t.Amount) ?? 0m;

                if (spentToday + input.Amount > card.DailyLimit)
                {
                    throw new UserFriendlyException(
                        $"Daily Limit exceeded. Limit={card.DailyLimit}, SpentToday={spentToday}, Amount={input.Amount}");
                }

                account.Withdraw(input.Amount);

                await _accounts.UpdateAsync(account, autoSave: true);

                await _tx.InsertAsync(new Transaction(
                    GuidGenerator.Create(),
                    TransactionType.DebitCardSpend,
                    input.Amount,
                    input.Description,
                    null,
                    card.Id,
                    null
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

    public async Task<CardSpendSummaryDto> GetDebitCardSpendSummaryAsync(string cardNo)
    {
        cardNo = NormalizeCardNo(cardNo);
        var card = await GetDebitCardOwnedByCardNoAsync(cardNo);

        var now = Clock.Now;
        var todayStart = now.Date;
        var tomorrow = todayStart.AddDays(1);

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var txQ = await _tx.GetQueryableAsync();

        var q = txQ.Where(t => t.DebitCardId == card.Id && t.TxType == TransactionType.DebitCardSpend);

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

    public async Task<PagedResultDto<DebitCardListItemDto>> GetMyDebitCardsAsync(MyDebitCardsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var q =
            from dc in debitCardsQ
            join a in accountsQ on dc.AccountId equals a.Id
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId
            select dc;

        if (input.AccountId.HasValue)
            q = q.Where(dc => dc.AccountId == input.AccountId.Value);

        if (!string.IsNullOrWhiteSpace(input.CardNo))
        {
            var n = NormalizeCardNo(input.CardNo);
            q = q.Where(dc => dc.CardNo == n);
        }

        var total = await AsyncExecuter.CountAsync(q);

        q = q.OrderBy(x => x.CardNo);

        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<DebitCardListItemDto>(
            total,
            items.Select(dc => new DebitCardListItemDto
            {
                Id = dc.Id,
                AccountId = dc.AccountId,
                CardNo = dc.CardNo,
                ExpireAt = dc.ExpireAt,
                DailyLimit = dc.DailyLimit,
                IsActive = dc.IsActive
            }).ToList()
        );
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
}
