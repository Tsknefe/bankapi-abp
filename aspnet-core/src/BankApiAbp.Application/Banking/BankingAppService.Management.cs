using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    [Authorize(BankingPermissions.Dashboard.Summary)]
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

        var recent = await AsyncExecuter.ToListAsync(
            qAcc.Concat(qDc).Concat(qCc)
               .OrderByDescending(x => x.CreationTime)
               .Take(lastTxCount)
        );

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
