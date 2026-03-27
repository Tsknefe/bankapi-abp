using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Banking.Events;
using BankApiAbp.Entities;
using BankApiAbp.Permissions;
using BankApiAbp.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.EventBus.Distributed;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    [Authorize(BankingPermissions.Accounts.Create)]
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
            throw new UserFriendlyException("Bu IBAN zaten mevcut");

        var account = new Account(
            GuidGenerator.Create(),
            input.CustomerId,
            input.Name,
            input.Iban,
            input.AccountType,
            input.InitialBalance
        );

        await _accounts.InsertAsync(account, autoSave: true);
        await _bankingCacheManager.InvalidateAccountsListAsync(userId);

        return new IdResponseDto { Id = account.Id };
    }

    [Authorize(BankingPermissions.Accounts.Deposit)]
    public async Task<DepositResultDto> DepositAsync(DepositDto input)
    {
        using var activity = ActivitySource.StartActivity("Banking.Deposit");
        activity?.SetTag("account.id", input.AccountId.ToString());
        activity?.SetTag("amount", input.Amount);

        var userId = CurrentUserIdOrThrow();
        var operation = "accounts.deposit";
        var key = GetIdempotencyKeyOrThrow(operation);
        var requestHash = BuildRequestHash(input.AccountId, input.Amount, input.Description);

        var (isDuplicate, record) =
            await _idem.TryBeginAsync(userId, operation, key, requestHash);

        if (isDuplicate)
        {
            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new BusinessException("IDEMPOTENCY_KEY_REUSE_WITH_DIFFERENT_PAYLOAD")
                    .WithData("message", "Aynı Idempotency-Key farklı istek gövdesi ile kullanılamaz.");
            }

            if (record.Status == "Completed" && record.ResponseJson != null)
            {
                var cached = JsonSerializer.Deserialize<DepositResultDto>(record.ResponseJson);
                if (cached != null) return cached;
            }

            await _idem.GetOrThrowDuplicateResponseAsync(record);
            throw new BusinessException("IDEMPOTENCY_UNKNOWN_STATE");
        }

        try
        {
            await using var handle = await _distributedLock.TryAcquireAsync(
                $"account:{input.AccountId}",
                TimeSpan.FromSeconds(10)
            );

            if (handle == null)
                throw new UserFriendlyException("Hesap şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");

            Guid txId = default;
            decimal newBalance = 0m;

            await _retry.ExecuteAsync(async ct =>
            {
                var account = await _rowLock.LockAccountForUpdateAsync(input.AccountId, ct);
                await EnsureAccountOwnedAsync(account.Id, ct);

                account.Deposit(input.Amount);
                await _accounts.UpdateAsync(account, autoSave: true);

                txId = GuidGenerator.Create();
                await _tx.InsertAsync(new Transaction(
                    txId,
                    TransactionType.Deposit,
                    input.Amount,
                    input.Description,
                    account.Id,
                    null,
                    null
                ), autoSave: true);

                var ledgerEntry = new LedgerEntry(
                    GuidGenerator.Create(),
                    txId,
                    account.Id,
                    LedgerDirection.Credit,
                    input.Amount,
                    account.Balance,
                    input.Description ?? "Deposit"
                );

                await _ledgerEntryRepository.InsertAsync(ledgerEntry, autoSave: true);

                newBalance = account.Balance;
            });

            var result = new DepositResultDto
            {
                TransactionId = txId,
                AccountId = input.AccountId,
                NewBalance = newBalance,
                IdempotencyKey = key,
                ProcessedAtUtc = Clock.Now
            };

            await _bankingCacheManager.InvalidateAccountReadModelsAsync(userId, input.AccountId);
            await _idem.CompleteAsync(record, result, 200);
            return result;
        }
        catch (Exception ex)
        {
            await _idem.FailAsync(record, ex);
            throw;
        }
    }

    [Authorize(BankingPermissions.Accounts.Withdraw)]
    public async Task<WithdrawResultDto> WithdrawAsync(WithdrawDto input)
    {
        using var activity = ActivitySource.StartActivity("Banking.Withdraw");
        activity?.SetTag("account.id", input.AccountId.ToString());
        activity?.SetTag("amount", input.Amount);

        var userId = CurrentUserIdOrThrow();
        var operation = "accounts.withdraw";
        var key = GetIdempotencyKeyOrThrow(operation);
        var requestHash = BuildRequestHash(input.AccountId, input.Amount, input.Description);

        var (isDuplicate, record) =
            await _idem.TryBeginAsync(userId, operation, key, requestHash);

        if (isDuplicate)
        {
            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new BusinessException("IDEMPOTENCY_KEY_REUSE_WITH_DIFFERENT_PAYLOAD")
                    .WithData("message", "Aynı Idempotency-Key farklı istek gövdesi ile kullanılamaz.");
            }

            if (record.Status == "Completed" && record.ResponseJson != null)
            {
                var cached = JsonSerializer.Deserialize<WithdrawResultDto>(record.ResponseJson);
                if (cached != null) return cached;
            }

            await _idem.GetOrThrowDuplicateResponseAsync(record);
            throw new BusinessException("IDEMPOTENCY_UNKNOWN_STATE");
        }

        try
        {
            await using var handle = await _distributedLock.TryAcquireAsync(
                $"account:{input.AccountId}",
                TimeSpan.FromSeconds(10)
            );

            if (handle == null)
                throw new UserFriendlyException("Hesap şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");

            Guid txId = default;
            decimal newBalance = 0m;

            await _retry.ExecuteAsync(async ct =>
            {
                var account = await _rowLock.LockAccountForUpdateAsync(input.AccountId, ct);
                await EnsureAccountOwnedAsync(account.Id, ct);

                account.Withdraw(input.Amount);
                await _accounts.UpdateAsync(account, autoSave: true);

                txId = GuidGenerator.Create();
                await _tx.InsertAsync(new Transaction(
                    txId,
                    TransactionType.Withdraw,
                    input.Amount,
                    input.Description,
                    account.Id,
                    null,
                    null
                ), autoSave: true);

                var ledgerEntry = new LedgerEntry(
                    GuidGenerator.Create(),
                    txId,
                    account.Id,
                    LedgerDirection.Debit,
                    input.Amount,
                    account.Balance,
                    input.Description ?? "Withdraw"
                );

                await _ledgerEntryRepository.InsertAsync(ledgerEntry, autoSave: true);

                newBalance = account.Balance;
            });

            var result = new WithdrawResultDto
            {
                TransactionId = txId,
                AccountId = input.AccountId,
                NewBalance = newBalance,
                IdempotencyKey = key,
                ProcessedAtUtc = Clock.Now
            };

            await _bankingCacheManager.InvalidateAccountReadModelsAsync(userId, input.AccountId);
            await _idem.CompleteAsync(record, result, 200);
            return result;
        }
        catch (Exception ex)
        {
            await _idem.FailAsync(record, ex);
            throw;
        }
    }

    [Authorize(BankingPermissions.Accounts.List)]
    public async Task<PagedResultDto<AccountListItemDto>> GetMyAccountsAsync(MyAccountsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var cached = await _bankingCacheManager.GetAccountsListAsync(userId, input);
        if (cached != null)
            return cached;

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

        var items = await AsyncExecuter.ToListAsync(
            q.Skip(input.SkipCount).Take(input.MaxResultCount)
        );

        var result = new PagedResultDto<AccountListItemDto>(
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

        await _bankingCacheManager.SetAccountsListAsync(userId, input, result);
        return result;
    }

    [Authorize(BankingPermissions.Accounts.Summary)]
    public async Task<AccountSummaryDto> GetAccountSummaryAsync(Guid accountId)
    {
        var userId = CurrentUserIdOrThrow();

        var acc = await GetAccountOwnedAsync(accountId);

        var cached = await _bankingCacheManager.GetSummaryAsync(userId, accountId);
        if (cached != null)
        {
            AccountSummaryCacheHitCounter.Add(1);
            return cached;
        }

        AccountSummaryCacheMissCounter.Add(1);

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

        var result = new AccountSummaryDto
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

        await _bankingCacheManager.SetSummaryAsync(userId, accountId, result);
        return result;
    }

    [Authorize(BankingPermissions.Accounts.Statement)]
    public async Task<PagedResultDto<TransactionDto>> GetAccountStatementAsync(GetAccountStatementInput input)
    {
        var userId = CurrentUserIdOrThrow();

        _ = await GetAccountOwnedAsync(input.AccountId);

        var cached = await _bankingCacheManager.GetStatementAsync(userId, input);
        if (cached != null)
        {
            AccountStatementCacheHitCounter.Add(1);
            return cached;
        }

        AccountStatementCacheMissCounter.Add(1);

        var txQ = await _tx.GetQueryableAsync();
        var q = txQ.Where(t => t.AccountId == input.AccountId);

        if (input.From.HasValue)
            q = q.Where(t => t.CreationTime >= input.From.Value);

        if (input.To.HasValue)
            q = q.Where(t => t.CreationTime <= input.To.Value);

        q = q.OrderByDescending(t => t.CreationTime);

        var total = await AsyncExecuter.CountAsync(q);
        var items = await AsyncExecuter.ToListAsync(
            q.Skip(input.SkipCount).Take(input.MaxResultCount)
        );

        var result = new PagedResultDto<TransactionDto>(
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

        await _bankingCacheManager.SetStatementAsync(userId, input, result);
        return result;
    }

    [Authorize(BankingPermissions.Accounts.Read)]
    public async Task<AccountDto> GetAccountAsync(Guid id)
    {
        var userId = CurrentUserIdOrThrow();

        var cached = await _bankingCacheManager.GetAccountAsync(userId, id);
        if (cached != null)
            return cached;

        var acc = await GetAccountOwnedAsync(id);

        var result = new AccountDto
        {
            Id = acc.Id,
            CustomerId = acc.CustomerId,
            Name = acc.Name,
            Iban = acc.Iban,
            Balance = acc.Balance,
            AccountType = acc.AccountType,
            IsActive = acc.IsActive
        };

        await _bankingCacheManager.SetAccountAsync(userId, id, result);
        return result;
    }

    [EnableRateLimiting("transfer")]
    [Authorize(BankingPermissions.Accounts.Transfer)]
    public async Task<TransferResultDto> TransferAsync(TransferDto input)
    {
        using var activity = ActivitySource.StartActivity("Banking.Transfer");
        activity?.SetTag("from.account", input.FromAccountId.ToString());
        activity?.SetTag("to.account", input.ToAccountId.ToString());
        activity?.SetTag("amount", input.Amount);

        var userId = CurrentUserIdOrThrow();
        var operation = "accounts.transfer";
        var key = GetIdempotencyKeyOrThrow(operation);

        if (input.FromAccountId == input.ToAccountId)
            throw new BusinessException("TRANSFER_SAME_ACCOUNT");

        var requestHash = BuildRequestHash(
            input.FromAccountId,
            input.ToAccountId,
            input.Amount,
            input.Description
        );

        var (isDuplicate, record) =
            await _idem.TryBeginAsync(userId, operation, key, requestHash);

        if (isDuplicate)
        {
            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new BusinessException("IDEMPOTENCY_KEY_REUSE_WITH_DIFFERENT_PAYLOAD")
                    .WithData("message", "Aynı Idempotency-Key farklı istek gövdesi ile kullanılamaz.");
            }

            if (record.Status == "Completed" && record.ResponseJson != null)
            {
                var cached = JsonSerializer.Deserialize<TransferResultDto>(record.ResponseJson);
                if (cached != null) return cached;
            }

            await _idem.GetOrThrowDuplicateResponseAsync(record);
            throw new BusinessException("IDEMPOTENCY_UNKNOWN_STATE");
        }

        try
        {
            _ = await GetAccountOwnedAsync(input.FromAccountId);
            _ = await GetAccountOwnedAsync(input.ToAccountId);

            var a = input.FromAccountId;
            var b = input.ToAccountId;
            var first = a.CompareTo(b) < 0 ? a : b;
            var second = a.CompareTo(b) < 0 ? b : a;

            await using var lock1 = await _distributedLock.TryAcquireAsync(
                $"account:{first}",
                TimeSpan.FromSeconds(10)
            );
            if (lock1 == null)
                throw new UserFriendlyException("Hesap şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");

            await using var lock2 = await _distributedLock.TryAcquireAsync(
                $"account:{second}",
                TimeSpan.FromSeconds(10)
            );
            if (lock2 == null)
                throw new UserFriendlyException("Hesap şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");

            Guid txOutId = default;
            Guid txInId = default;
            decimal fromNew = 0m;
            decimal toNew = 0m;

            await _retry.ExecuteAsync(async ct =>
            {
                var firstAcc = await _rowLock.LockAccountForUpdateAsync(first, ct);
                var secondAcc = await _rowLock.LockAccountForUpdateAsync(second, ct);

                var fromAcc = input.FromAccountId == first ? firstAcc : secondAcc;
                var toAcc = input.ToAccountId == first ? firstAcc : secondAcc;

                await EnsureAccountOwnedAsync(fromAcc.Id, ct);
                await EnsureAccountOwnedAsync(toAcc.Id, ct);

                if (fromAcc.Balance < input.Amount)
                    throw new BusinessException("INSUFFICIENT_BALANCE")
                        .WithData("Balance", fromAcc.Balance)
                        .WithData("Amount", input.Amount);

                fromAcc.Withdraw(input.Amount);
                toAcc.Deposit(input.Amount);

                await _accounts.UpdateAsync(fromAcc, autoSave: true);
                await _accounts.UpdateAsync(toAcc, autoSave: true);

                txOutId = GuidGenerator.Create();
                txInId = GuidGenerator.Create();

                await _tx.InsertAsync(new Transaction(
                    txOutId,
                    TransactionType.TransferOut,
                    input.Amount,
                    input.Description ?? $"Transfer to {toAcc.Iban}",
                    fromAcc.Id,
                    null,
                    null
                ), autoSave: true);

                await _tx.InsertAsync(new Transaction(
                    txInId,
                    TransactionType.TransferIn,
                    input.Amount,
                    input.Description ?? $"Transfer from {fromAcc.Iban}",
                    toAcc.Id,
                    null,
                    null
                ), autoSave: true);

                var debitEntry = new LedgerEntry(
                    GuidGenerator.Create(),
                    txOutId,
                    fromAcc.Id,
                    LedgerDirection.Debit,
                    input.Amount,
                    fromAcc.Balance,
                    input.Description ?? $"Transfer to {toAcc.Iban}"
                );

                var creditEntry = new LedgerEntry(
                    GuidGenerator.Create(),
                    txInId,
                    toAcc.Id,
                    LedgerDirection.Credit,
                    input.Amount,
                    toAcc.Balance,
                    input.Description ?? $"Transfer from {fromAcc.Iban}"
                );

                await _ledgerEntryRepository.InsertAsync(debitEntry, autoSave: true);
                await _ledgerEntryRepository.InsertAsync(creditEntry, autoSave: true);

                fromNew = fromAcc.Balance;
                toNew = toAcc.Balance;
            });

            var result = new TransferResultDto
            {
                TransactionOutId = txOutId,
                TransactionInId = txInId,
                FromAccountId = input.FromAccountId,
                ToAccountId = input.ToAccountId,
                Amount = input.Amount,
                FromNewBalance = fromNew,
                ToNewBalance = toNew,
                IdempotencyKey = key,
                ProcessedAtUtc = Clock.Now
            };

            await _bankingCacheManager.InvalidateAccountReadModelsAsync(userId, input.FromAccountId);
            await _bankingCacheManager.InvalidateAccountReadModelsAsync(userId, input.ToAccountId);

            await _distributedEventBus.PublishAsync(
                new TransferCompletedEto
                {
                    TransferId = txOutId,
                    FromAccountId = input.FromAccountId,
                    ToAccountId = input.ToAccountId,
                    Amount = input.Amount,
                    Description = input.Description,
                    OccurredAtUtc = Clock.Now,
                    IdempotencyKey = key,
                    UserId = userId
                }
            );
            await _idem.CompleteAsync(record, result, 200);
            return result;
        }
        catch (Exception ex)
        {
            await _idem.FailAsync(record, ex);
            throw;
        }
    }
}