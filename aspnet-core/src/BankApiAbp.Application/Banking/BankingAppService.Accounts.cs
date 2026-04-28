using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Banking.Infrastructure;
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
        var startedAt = Stopwatch.GetTimestamp();
        DepositRequestCounter.Add(1);

        using var activity = ActivitySource.StartActivity("Banking.Deposit");
        activity?.SetTag("account.id", input.AccountId.ToString());
        activity?.SetTag("amount", input.Amount);

        var userId = CurrentUserIdOrThrow();
        var operation = "accounts.deposit";
        var key = GetIdempotencyKeyOrThrow(operation);
        var requestHash = BuildRequestHash(input.AccountId, input.Amount, input.Description);

        using var idemSpan = ActivitySource.StartActivity("Banking.Deposit.Idempotency");

        var (isDuplicate, record) =
            await _idem.TryBeginAsync(userId, operation, key, requestHash);

        idemSpan?.SetTag("idempotency.key", key);
        idemSpan?.SetTag("idempotency.duplicate", isDuplicate);

        if (isDuplicate)
        {
            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                DepositFailureCounter.Add(1);
                DepositDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

                throw new BusinessException("IDEMPOTENCY_KEY_REUSE_WITH_DIFFERENT_PAYLOAD")
                    .WithData("message", "Aynı Idempotency-Key farklı istek gövdesi ile kullanılamaz.");
            }

            if (record.Status == "Completed" && record.ResponseJson != null)
            {
                var cached = JsonSerializer.Deserialize<DepositResultDto>(record.ResponseJson);
                if (cached != null)
                {
                    DepositSuccessCounter.Add(1);
                    DepositDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    return cached;
                }
            }

            DepositFailureCounter.Add(1);
            DepositDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            await _idem.GetOrThrowDuplicateResponseAsync(record);
            throw new BusinessException("IDEMPOTENCY_UNKNOWN_STATE");
        }

        try
        {
            using var lockSpan = ActivitySource.StartActivity("Banking.Deposit.Lock");

            await using var handle = await _distributedLock.TryAcquireAsync(
                $"account:{input.AccountId}",
                TimeSpan.FromSeconds(10)
            );

            if (handle == null)
            {
                lockSpan?.SetTag("lock.acquired", false);
                throw new UserFriendlyException("Hesap şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");
            }

            lockSpan?.SetTag("lock.acquired", true);

            Guid txId = default;
            decimal newBalance = 0m;

            using var dbSpan = ActivitySource.StartActivity("Banking.Deposit.DatabaseWork");

            await _retry.ExecuteAsync(async ct =>
            {
                var account = await _rowLock.LockAccountForUpdateAsync(input.AccountId, ct);
                await EnsureAccountOwnedAsync(account.Id, ct);

                dbSpan?.SetTag("balance.before", account.Balance);

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

                dbSpan?.SetTag("transaction.id", txId.ToString());
                dbSpan?.SetTag("balance.after", newBalance);
            });

            using (var cacheSpan = ActivitySource.StartActivity("Banking.Deposit.CacheInvalidate"))
            {
                await _bankingCacheManager.InvalidateAccountReadModelsAsync(userId, input.AccountId);
                cacheSpan?.SetTag("cache.invalidated", true);
                cacheSpan?.SetTag("account.id", input.AccountId.ToString());
            }

            var result = new DepositResultDto
            {
                TransactionId = txId,
                AccountId = input.AccountId,
                NewBalance = newBalance,
                IdempotencyKey = key,
                ProcessedAtUtc = Clock.Now
            };

            await _idem.CompleteAsync(record, result, 200);

            DepositSuccessCounter.Add(1);
            DepositDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", true);
            activity?.SetTag("exception", ex.Message);

            DepositFailureCounter.Add(1);
            DepositDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            await _idem.FailAsync(record, ex);
            throw;
        }
    }

    [Authorize(BankingPermissions.Accounts.Withdraw)]
    public async Task<WithdrawResultDto> WithdrawAsync(WithdrawDto input)
    {
        var startedAt = Stopwatch.GetTimestamp();
        WithdrawRequestCounter.Add(1);

        using var activity = ActivitySource.StartActivity("Banking.Withdraw");
        activity?.SetTag("account.id", input.AccountId.ToString());
        activity?.SetTag("amount", input.Amount);

        var userId = CurrentUserIdOrThrow();
        var operation = "accounts.withdraw";
        var key = GetIdempotencyKeyOrThrow(operation);
        var requestHash = BuildRequestHash(input.AccountId, input.Amount, input.Description);

        using var idemSpan = ActivitySource.StartActivity("Banking.Withdraw.Idempotency");

        var (isDuplicate, record) =
            await _idem.TryBeginAsync(userId, operation, key, requestHash);

        idemSpan?.SetTag("idempotency.key", key);
        idemSpan?.SetTag("idempotency.duplicate", isDuplicate);

        if (isDuplicate)
        {
            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                WithdrawFailureCounter.Add(1);
                WithdrawDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

                throw new BusinessException("IDEMPOTENCY_KEY_REUSE_WITH_DIFFERENT_PAYLOAD")
                    .WithData("message", "Aynı Idempotency-Key farklı istek gövdesi ile kullanılamaz.");
            }

            if (record.Status == "Completed" && record.ResponseJson != null)
            {
                var cached = JsonSerializer.Deserialize<WithdrawResultDto>(record.ResponseJson);
                if (cached != null)
                {
                    WithdrawSuccessCounter.Add(1);
                    WithdrawDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    return cached;
                }
            }

            WithdrawFailureCounter.Add(1);
            WithdrawDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            await _idem.GetOrThrowDuplicateResponseAsync(record);
            throw new BusinessException("IDEMPOTENCY_UNKNOWN_STATE");
        }

        try
        {
            using var lockSpan = ActivitySource.StartActivity("Banking.Withdraw.Lock");

            await using var handle = await _distributedLock.TryAcquireAsync(
                $"account:{input.AccountId}",
                TimeSpan.FromSeconds(10)
            );

            if (handle == null)
            {
                lockSpan?.SetTag("lock.acquired", false);
                throw new UserFriendlyException("Hesap şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");
            }

            lockSpan?.SetTag("lock.acquired", true);

            Guid txId = default;
            decimal newBalance = 0m;

            using var dbSpan = ActivitySource.StartActivity("Banking.Withdraw.DatabaseWork");

            await _retry.ExecuteAsync(async ct =>
            {
                var account = await _rowLock.LockAccountForUpdateAsync(input.AccountId, ct);
                await EnsureAccountOwnedAsync(account.Id, ct);

                dbSpan?.SetTag("balance.before", account.Balance);

                if (account.Balance < input.Amount)
                {
                    throw new BusinessException("INSUFFICIENT_BALANCE")
                        .WithData("AccountId", account.Id)
                        .WithData("Balance", account.Balance)
                        .WithData("RequestedAmount", input.Amount);
                }

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

                dbSpan?.SetTag("transaction.id", txId.ToString());
                dbSpan?.SetTag("balance.after", newBalance);
            });

            using (var cacheSpan = ActivitySource.StartActivity("Banking.Withdraw.CacheInvalidate"))
            {
                await _bankingCacheManager.InvalidateAccountReadModelsAsync(userId, input.AccountId);
                cacheSpan?.SetTag("cache.invalidated", true);
                cacheSpan?.SetTag("account.id", input.AccountId.ToString());
            }

            var result = new WithdrawResultDto
            {
                TransactionId = txId,
                AccountId = input.AccountId,
                NewBalance = newBalance,
                IdempotencyKey = key,
                ProcessedAtUtc = Clock.Now
            };

            await _idem.CompleteAsync(record, result, 200);

            WithdrawSuccessCounter.Add(1);
            WithdrawDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", true);
            activity?.SetTag("exception", ex.Message);

            WithdrawFailureCounter.Add(1);
            WithdrawDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

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
        var startedAt = Stopwatch.GetTimestamp();
        TransferRequestCounter.Add(1);

        using var activity = ActivitySource.StartActivity("Banking.Transfer");
        activity?.SetTag("from.account", input.FromAccountId.ToString());
        activity?.SetTag("to.account", input.ToAccountId.ToString());
        activity?.SetTag("amount", input.Amount);

        var userId = CurrentUserIdOrThrow();
        var operation = "accounts.transfer";
        var key = GetIdempotencyKeyOrThrow(operation);

        if (input.FromAccountId == input.ToAccountId)
        {
            TransferFailureCounter.Add(1);
            TransferDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw new BusinessException("TRANSFER_SAME_ACCOUNT");
        }

        var requestHash = BuildRequestHash(
            input.FromAccountId,
            input.ToAccountId,
            input.Amount,
            input.Description
        );

        using var idemSpan = ActivitySource.StartActivity("Banking.Transfer.Idempotency");

        var (isDuplicate, record) =
            await _idem.TryBeginAsync(userId, operation, key, requestHash);

        idemSpan?.SetTag("idempotency.key", key);
        idemSpan?.SetTag("idempotency.duplicate", isDuplicate);

        if (isDuplicate)
        {
            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                TransferFailureCounter.Add(1);
                TransferDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                throw new BusinessException("IDEMPOTENCY_KEY_REUSE_WITH_DIFFERENT_PAYLOAD");
            }

            if (record.Status == "Completed" && record.ResponseJson != null)
            {
                var cached = JsonSerializer.Deserialize<TransferResultDto>(record.ResponseJson);
                if (cached != null)
                {
                    TransferSuccessCounter.Add(1);
                    TransferDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    return cached;
                }
            }

            TransferFailureCounter.Add(1);
            TransferDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

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

            using var lockSpan = ActivitySource.StartActivity("Banking.Transfer.Locks");

            await using var lock1 = await _distributedLock.TryAcquireAsync(
                $"account:{first}",
                TimeSpan.FromSeconds(10)
            );

            if (lock1 == null)
            {
                TransferLockFailureCounter.Add(1);
                lockSpan?.SetTag("lock.first", false);
                throw new UserFriendlyException("Hesap kilitlenemedi");
            }

            lockSpan?.SetTag("lock.first", true);

            await using var lock2 = await _distributedLock.TryAcquireAsync(
                $"account:{second}",
                TimeSpan.FromSeconds(10)
            );

            if (lock2 == null)
            {
                TransferLockFailureCounter.Add(1);
                lockSpan?.SetTag("lock.second", false);
                throw new UserFriendlyException("Hesap kilitlenemedi");
            }

            lockSpan?.SetTag("lock.second", true);

            Guid txOutId = default;
            Guid txInId = default;
            decimal fromNew = 0m;
            decimal toNew = 0m;

            using var dbSpan = ActivitySource.StartActivity("Banking.Transfer.DatabaseWork");

            await _retry.ExecuteAsync(async ct =>
            {
                if (_testFaultInjection.ShouldThrowTransientFailure())
                    throw new SimulatedTransientException();

                var firstAcc = await _rowLock.LockAccountForUpdateAsync(first, ct);
                var secondAcc = await _rowLock.LockAccountForUpdateAsync(second, ct);

                var fromAcc = input.FromAccountId == first ? firstAcc : secondAcc;
                var toAcc = input.ToAccountId == first ? firstAcc : secondAcc;

                dbSpan?.SetTag("from.balance.before", fromAcc.Balance);
                dbSpan?.SetTag("to.balance.before", toAcc.Balance);

                await EnsureAccountOwnedAsync(fromAcc.Id, ct);
                await EnsureAccountOwnedAsync(toAcc.Id, ct);

                if (fromAcc.Balance < input.Amount)
                {
                    dbSpan?.SetTag("error", "INSUFFICIENT_BALANCE");
                    throw new BusinessException("INSUFFICIENT_BALANCE");
                }

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
                    input.Description,
                    fromAcc.Id,
                    null,
                    null
                ), autoSave: true);

                await _tx.InsertAsync(new Transaction(
                    txInId,
                    TransactionType.TransferIn,
                    input.Amount,
                    input.Description,
                    toAcc.Id,
                    null,
                    null
                ), autoSave: true);

                fromNew = fromAcc.Balance;
                toNew = toAcc.Balance;

                dbSpan?.SetTag("from.balance.after", fromNew);
                dbSpan?.SetTag("to.balance.after", toNew);
            });

            using (var eventSpan = ActivitySource.StartActivity("Banking.Transfer.PublishEvent"))
            {
                var current = Activity.Current;

                await _distributedEventBus.PublishAsync(
                    new MoneyTransferredEto
                    {
                        EventId = Guid.NewGuid(),
                        TransferId = txOutId,
                        FromAccountId = input.FromAccountId,
                        ToAccountId = input.ToAccountId,
                        Amount = input.Amount,
                        Description = input.Description,
                        OccurredAtUtc = Clock.Now,
                        IdempotencyKey = key,
                        UserId = userId,
                        TraceParent = current?.Id,
                        TraceState = current?.TraceStateString
                    }
                );

                TransferPublishedEventCounter.Add(1);
                eventSpan?.SetTag("event.published", true);
                eventSpan?.SetTag("transfer.id", txOutId.ToString());
            }

            using (var cacheSpan = ActivitySource.StartActivity("Banking.Transfer.CacheInvalidate"))
            {
                await _bankingCacheManager.InvalidateAccountReadModelsAsync(userId, input.FromAccountId);
                await _bankingCacheManager.InvalidateAccountReadModelsAsync(userId, input.ToAccountId);

                cacheSpan?.SetTag("cache.invalidated", true);
                cacheSpan?.SetTag("from.account", input.FromAccountId.ToString());
                cacheSpan?.SetTag("to.account", input.ToAccountId.ToString());
            }

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

            await _idem.CompleteAsync(record, result, 200);

            TransferSuccessCounter.Add(1);
            TransferDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", true);
            activity?.SetTag("exception", ex.Message);

            TransferFailureCounter.Add(1);
            TransferDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            await _idem.FailAsync(record, ex);
            throw;
        }
    }
}