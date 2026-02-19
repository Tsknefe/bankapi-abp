using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Entities;
using BankApiAbp.Transactions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    public async Task<IdResponseDto> CreateAccountAsync(CreateAccountDto input)
    {
        var userId = CurrentUserIdOrThrow();

        _ = await GetCustomerOwnedAsync(input.CustomerId);

        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var ibanExistsForUser = await AsyncExecuter.AnyAsync(
            from a in accountsQ
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId && a.Iban == input.Iban
            select a.Id
        );

        if (ibanExistsForUser)
            throw new UserFriendlyException("Bu IBAN zaten mevcut ");

        var account = new Account(
            GuidGenerator.Create(),
            input.CustomerId,
            input.Name,
            input.Iban,
            input.AccountType,
            input.InitialBalance
        );

        await _accounts.InsertAsync(account, autoSave: true);
        return new IdResponseDto { Id = account.Id };
    }

    public async Task DepositAsync(DepositDto input)
    {
        var account = await GetAccountOwnedAsync(input.AccountId);

        account.Deposit(input.Amount);

        await _accounts.UpdateAsync(account, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.Deposit,
            input.Amount,
            input.Description,
            input.AccountId,
            null,
            null
        ), autoSave: true);
    }

    public async Task WithdrawAsync(WithdrawDto input)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var account = await GetAccountOwnedAsync(input.AccountId);

                account.Withdraw(input.Amount);

                await _accounts.UpdateAsync(account, autoSave: true);

                await _tx.InsertAsync(new Transaction(
                    GuidGenerator.Create(),
                    TransactionType.Withdraw,
                    input.Amount,
                    input.Description,
                    input.AccountId,
                    null,
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

    public async Task<AccountDto> GetAccountAsync(Guid id)
    {
        var acc = await GetAccountOwnedAsync(id);

        return new AccountDto
        {
            Id = acc.Id,
            CustomerId = acc.CustomerId,
            Name = acc.Name,
            Iban = acc.Iban,
            Balance = acc.Balance,
            AccountType = acc.AccountType,
            IsActive = acc.IsActive,
        };
    }

    public async Task<AccountSummaryDto> GetAccountSummaryAsync(Guid accountId)
    {
        var acc = await GetAccountOwnedAsync(accountId);

        var now = Clock.Now;
        var todayStart = now.Date;
        var tomorrow = todayStart.AddDays(1);

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var txQ = await _tx.GetQueryableAsync();

        var qAcc = txQ.Where(t => t.AccountId == acc.Id);

        var todayQ = qAcc.Where(t => t.CreationTime >= todayStart && t.CreationTime < tomorrow);
        var todayCount = await AsyncExecuter.CountAsync(todayQ);

        var todayIn = await AsyncExecuter.SumAsync(
            todayQ.Where(t => t.TxType == TransactionType.Deposit),
            t => (decimal?)t.Amount) ?? 0m;

        var todayOut = await AsyncExecuter.SumAsync(
            todayQ.Where(t => t.TxType == TransactionType.Withdraw),
            t => (decimal?)t.Amount) ?? 0m;

        var monthQ = qAcc.Where(t => t.CreationTime >= monthStart && t.CreationTime < nextMonth);
        var monthCount = await AsyncExecuter.CountAsync(monthQ);

        var monthIn = await AsyncExecuter.SumAsync(
            monthQ.Where(t => t.TxType == TransactionType.Deposit),
            t => (decimal?)t.Amount) ?? 0m;

        var monthOut = await AsyncExecuter.SumAsync(
            monthQ.Where(t => t.TxType == TransactionType.Withdraw),
            t => (decimal?)t.Amount) ?? 0m;

        return new AccountSummaryDto
        {
            AccountId = acc.Id,
            Balance = acc.Balance,

            Today = todayStart,
            TodayTxCount = todayCount,
            TodayInTotal = todayIn,
            TodayOutTotal = todayOut,

            MonthStart = monthStart,
            MonthTxCount = monthCount,
            MonthInTotal = monthIn,
            MonthOutTotal = monthOut
        };
    }

    public async Task<PagedResultDto<TransactionDto>> GetAccountStatementAsync(GetAccountStatementInput input)
    {
        _ = await GetAccountOwnedAsync(input.AccountId);

        var txQ = await _tx.GetQueryableAsync();

        var q = txQ.Where(t => t.AccountId == input.AccountId);

        if (input.From.HasValue)
            q = q.Where(t => t.CreationTime >= input.From.Value);

        if (input.To.HasValue)
            q = q.Where(t => t.CreationTime <= input.To.Value);

        q = q.OrderByDescending(t => t.CreationTime);

        var total = await AsyncExecuter.CountAsync(q);
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

    public async Task<PagedResultDto<AccountListItemDto>> GetMyAccountsAsync(MyAccountsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var q =
            from a in accountsQ
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId
            select a;

        if (input.CustomerId.HasValue)
            q = q.Where(a => a.CustomerId == input.CustomerId.Value);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            q = q.Where(a => a.Name.Contains(f) || a.Iban.Contains(f));
        }

        var total = await AsyncExecuter.CountAsync(q);

        q = q.OrderBy(x => x.Name);

        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<AccountListItemDto>(
            total,
            items.Select(a => new AccountListItemDto
            {
                Id = a.Id,
                CustomerId = a.CustomerId,
                Name = a.Name,
                Iban = a.Iban,
                Balance = a.Balance,
                AccountType = a.AccountType,
                IsActive = a.IsActive
            }).ToList()
        );
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
}
