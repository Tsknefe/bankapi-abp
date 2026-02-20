using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    [Authorize(BankingPermissions.Transactions.List)]
    public async Task<PagedResultDto<TransactionDto>> GetTransactionsAsync(GetTransactionsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var txQ = await _tx.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();
        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var creditCardsQ = await _creditCards.GetQueryableAsync();

        var baseQ =
            from t in txQ
            join a in accountsQ on t.AccountId equals a.Id into aJoin
            from a in aJoin.DefaultIfEmpty()
            join cA in customersQ on a.CustomerId equals cA.Id into cAJoin
            from cA in cAJoin.DefaultIfEmpty()

            join dc in debitCardsQ on t.DebitCardId equals dc.Id into dcJoin
            from dc in dcJoin.DefaultIfEmpty()
            join aDc in accountsQ on dc.AccountId equals aDc.Id into aDcJoin
            from aDc in aDcJoin.DefaultIfEmpty()
            join cDc in customersQ on aDc.CustomerId equals cDc.Id into cDcJoin
            from cDc in cDcJoin.DefaultIfEmpty()

            join cc in creditCardsQ on t.CreditCardId equals cc.Id into ccJoin
            from cc in ccJoin.DefaultIfEmpty()
            join cC in customersQ on cc.CustomerId equals cC.Id into cCJoin
            from cC in cCJoin.DefaultIfEmpty()

            let ownerUserId =
                t.AccountId != null ? cA.UserId :
                t.DebitCardId != null ? cDc.UserId :
                cC.UserId

            where ownerUserId == userId
            select t;

        var q = baseQ;

        if (input.AccountId.HasValue)
            q = q.Where(x => x.AccountId == input.AccountId);

        if (input.DebitCardId.HasValue)
            q = q.Where(x => x.DebitCardId == input.DebitCardId);

        if (input.CreditCardId.HasValue)
            q = q.Where(x => x.CreditCardId == input.CreditCardId);

        if (input.From.HasValue)
            q = q.Where(x => x.CreationTime >= input.From.Value);

        if (input.To.HasValue)
            q = q.Where(x => x.CreationTime <= input.To.Value);

        var total = await AsyncExecuter.CountAsync(q);

        q = q.OrderByDescending(x => x.CreationTime);

        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<TransactionDto>(
            total,
            items.Select(t => new TransactionDto
            {
                Id = t.Id,
                TxType = t.TxType,
                Amount = t.Amount,
                Description = t.Description,
                CreationTime = t.CreationTime,
                AccountId = t.AccountId,
                DebitCardId = t.DebitCardId,
                CreditCardId = t.CreditCardId
            }).ToList()
        );
    }

    [Authorize(BankingPermissions.Transactions.List)]
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

        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

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
}
