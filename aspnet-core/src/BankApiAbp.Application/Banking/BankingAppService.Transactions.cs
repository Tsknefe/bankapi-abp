using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    public async Task<PagedResultDto<TransactionListItemDto>> GetMyTransactionsAsync(MyTransactionsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var txQ = await _tx.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();
        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var creditCardsQ = await _creditCards.GetQueryableAsync();

        var qAcc =
            from t in txQ
            join a in accountsQ on t.AccountId equals a.Id
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId
            select new { t, OwnerType = "Account", Iban = a.Iban, CardNo = (string?)null, CustomerName = c.Name };

        var qDc =
            from t in txQ
            join dc in debitCardsQ on t.DebitCardId equals dc.Id
            join a in accountsQ on dc.AccountId equals a.Id
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId
            select new { t, OwnerType = "DebitCard", Iban = (string?)null, CardNo = dc.CardNo, CustomerName = c.Name };

        var qCc =
            from t in txQ
            join cc in creditCardsQ on t.CreditCardId equals cc.Id
            join c in customersQ on cc.CustomerId equals c.Id
            where c.UserId == userId
            select new { t, OwnerType = "CreditCard", Iban = (string?)null, CardNo = cc.CardNo, CustomerName = c.Name };

        var q = qAcc.Concat(qDc).Concat(qCc);

        if (input.AccountId.HasValue)
            q = q.Where(x => x.t.AccountId == input.AccountId.Value);

        if (input.DebitCardId.HasValue)
            q = q.Where(x => x.t.DebitCardId == input.DebitCardId.Value);

        if (input.CreditCardId.HasValue)
            q = q.Where(x => x.t.CreditCardId == input.CreditCardId.Value);

        if (input.From.HasValue)
            q = q.Where(x => x.t.CreationTime >= input.From.Value);

        if (input.To.HasValue)
            q = q.Where(x => x.t.CreationTime <= input.To.Value);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            q = q.Where(x =>
                (x.t.Description != null && x.t.Description.Contains(f)) ||
                (x.Iban != null && x.Iban.Contains(f)) ||
                (x.CardNo != null && x.CardNo.Contains(f)) ||
                (x.CustomerName != null && x.CustomerName.Contains(f)));
        }

        var total = await AsyncExecuter.CountAsync(q);

        q = q.OrderByDescending(x => x.t.CreationTime);

        var items = await AsyncExecuter.ToListAsync(
            q.Skip(input.SkipCount).Take(input.MaxResultCount)
        );

        return new PagedResultDto<TransactionListItemDto>(
            total,
            items.Select(x => new TransactionListItemDto
            {
                Id = x.t.Id,
                OwnerType = x.OwnerType,

                AccountId = x.t.AccountId,
                DebitCardId = x.t.DebitCardId,
                CreditCardId = x.t.CreditCardId,

                Iban = x.Iban,
                CardNo = x.CardNo,
                CustomerName = x.CustomerName,

                TxType = (int)x.t.TxType,
                Amount = x.t.Amount,
                Description = x.t.Description,
                CreationTime = x.t.CreationTime
            }).ToList()
        );
    }

    public async Task<BankingSummaryDto> GetMySummaryAsync(int lastTxCount = 10)
    {
        var userId = CurrentUserIdOrThrow();
        if (lastTxCount <= 0) lastTxCount = 10;
        if (lastTxCount > 50) lastTxCount = 50;

        var customersQ = await _customers.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var creditCardsQ = await _creditCards.GetQueryableAsync();
        var txQ = await _tx.GetQueryableAsync();

        var myCustomersQ = customersQ.Where(c => c.UserId == userId);

        var myAccountsQ =
            from a in accountsQ
            join c in myCustomersQ on a.CustomerId equals c.Id
            select a;

        var myDebitCardsQ =
            from dc in debitCardsQ
            join a in myAccountsQ on dc.AccountId equals a.Id
            select dc;

        var myCreditCardsQ =
            from cc in creditCardsQ
            join c in myCustomersQ on cc.CustomerId equals c.Id
            select cc;

        var totalBalance = await AsyncExecuter.SumAsync(myAccountsQ, a => (decimal?)a.Balance) ?? 0m;
        var totalDebt = await AsyncExecuter.SumAsync(myCreditCardsQ, cc => (decimal?)cc.CurrentDebt) ?? 0m;

        var accountsCount = await AsyncExecuter.CountAsync(myAccountsQ);
        var debitCount = await AsyncExecuter.CountAsync(myDebitCardsQ);
        var creditCount = await AsyncExecuter.CountAsync(myCreditCardsQ);

        var qAcc =
            from t in txQ
            join a in accountsQ on t.AccountId equals a.Id
            join c in myCustomersQ on a.CustomerId equals c.Id
            select new RecentTransactionDto
            {
                Id = t.Id,
                OwnerType = "Account",
                Iban = a.Iban,
                CardNo = null,
                CustomerName = c.Name,
                TxType = (int)t.TxType,
                Amount = t.Amount,
                Description = t.Description,
                CreationTime = t.CreationTime
            };

        var qDc =
            from t in txQ
            join dc in debitCardsQ on t.DebitCardId equals dc.Id
            join a in accountsQ on dc.AccountId equals a.Id
            join c in myCustomersQ on a.CustomerId equals c.Id
            select new RecentTransactionDto
            {
                Id = t.Id,
                OwnerType = "DebitCard",
                Iban = null,
                CardNo = dc.CardNo,
                CustomerName = c.Name,
                TxType = (int)t.TxType,
                Amount = t.Amount,
                Description = t.Description,
                CreationTime = t.CreationTime
            };

        var qCc =
            from t in txQ
            join cc in creditCardsQ on t.CreditCardId equals cc.Id
            join c in myCustomersQ on cc.CustomerId equals c.Id
            select new RecentTransactionDto
            {
                Id = t.Id,
                OwnerType = "CreditCard",
                Iban = null,
                CardNo = cc.CardNo,
                CustomerName = c.Name,
                TxType = (int)t.TxType,
                Amount = t.Amount,
                Description = t.Description,
                CreationTime = t.CreationTime
            };

        var recentQ = qAcc.Concat(qDc).Concat(qCc)
            .OrderByDescending(x => x.CreationTime)
            .Take(lastTxCount);

        var recent = await AsyncExecuter.ToListAsync(recentQ);

        return new BankingSummaryDto
        {
            TotalBalance = totalBalance,
            TotalCreditDebt = totalDebt,
            AccountsCount = accountsCount,
            DebitCardsCount = debitCount,
            CreditCardsCount = creditCount,
            RecentTransactions = recent
        };
    }
}
