using System;
using System.Globalization;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;

namespace BankApiAbp.Banking.Caching;

public class BankingCacheManager : IBankingCacheManager, ITransientDependency
{
    private readonly IDistributedCache<AccountSummaryDto> _summaryCache;
    private readonly IDistributedCache<PagedResultDto<TransactionDto>> _statementCache;
    private readonly IDistributedCache<string> _versionCache;
    private readonly IDistributedCache<AccountDto> _accountCache;
    private readonly IDistributedCache<PagedResultDto<AccountListItemDto>> _accountsListCache;
    private readonly ILogger<BankingCacheManager> _logger;

    public BankingCacheManager(
        IDistributedCache<AccountSummaryDto> summaryCache,
        IDistributedCache<PagedResultDto<TransactionDto>> statementCache,
        IDistributedCache<string> versionCache,
        IDistributedCache<AccountDto> accountCache,
        IDistributedCache<PagedResultDto<AccountListItemDto>> accountsListCache,
        ILogger<BankingCacheManager> logger)
    {
        _summaryCache = summaryCache;
        _statementCache = statementCache;
        _versionCache = versionCache;
        _accountCache = accountCache;
        _accountsListCache = accountsListCache;
        _logger = logger;
    }

    private static DistributedCacheEntryOptions AccountTtl =>
        new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
        };

    private static DistributedCacheEntryOptions AccountsListTtl =>
        new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
        };

    private static DistributedCacheEntryOptions SummaryTtl =>
        new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
        };

    private static DistributedCacheEntryOptions StatementTtl =>
        new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
        };

    private static DistributedCacheEntryOptions VersionTtl =>
        new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30)
        };

    private string AccountKey(Guid userId, Guid accountId, int version)
        => $"acct:detail:u:{userId}:a:{accountId}:v:{version}";

    private string AccountsListVersionKey(Guid userId)
        => $"acct:listver:u:{userId}";

    private string AccountsListKey(Guid userId, MyAccountsInput input, int version)
        => $"acct:list:u:{userId}:v:{version}"
           + $":c:{(input.CustomerId.HasValue ? input.CustomerId.Value.ToString() : "null")}"
           + $":f:{(string.IsNullOrWhiteSpace(input.Filter) ? "null" : input.Filter.Trim())}"
           + $":s:{input.SkipCount}:m:{input.MaxResultCount}";

    private string SummaryKey(Guid userId, Guid accountId, int version)
        => $"acct:summary:u:{userId}:a:{accountId}:v:{version}";

    private string StatementKey(Guid userId, GetAccountStatementInput input, int version)
        => $"acct:stmt:u:{userId}:a:{input.AccountId}:v:{version}"
           + $":f:{(input.From.HasValue ? input.From.Value.ToString("O", CultureInfo.InvariantCulture) : "null")}"
           + $":t:{(input.To.HasValue ? input.To.Value.ToString("O", CultureInfo.InvariantCulture) : "null")}"
           + $":s:{input.SkipCount}:m:{input.MaxResultCount}";

    private string VersionKey(Guid userId, Guid accountId)
        => $"acct:ver:u:{userId}:a:{accountId}";

    public async Task<int> GetReadModelVersionAsync(Guid userId, Guid accountId)
    {
        var v = await _versionCache.GetAsync(VersionKey(userId, accountId));
        if (string.IsNullOrWhiteSpace(v))
            return 1;

        return int.TryParse(v, out var parsed) ? parsed : 1;
    }

    private async Task<int> GetAccountsListVersionAsync(Guid userId)
    {
        var v = await _versionCache.GetAsync(AccountsListVersionKey(userId));
        if (string.IsNullOrWhiteSpace(v))
            return 1;

        return int.TryParse(v, out var parsed) ? parsed : 1;
    }

    private async Task BumpReadModelVersionAsync(Guid userId, Guid accountId)
    {
        var current = await GetReadModelVersionAsync(userId, accountId);
        var next = current + 1;

        await _versionCache.SetAsync(
            VersionKey(userId, accountId),
            next.ToString(CultureInfo.InvariantCulture),
            VersionTtl
        );
    }

    private async Task BumpAccountsListVersionAsync(Guid userId)
    {
        var current = await GetAccountsListVersionAsync(userId);
        var next = current + 1;

        await _versionCache.SetAsync(
            AccountsListVersionKey(userId),
            next.ToString(CultureInfo.InvariantCulture),
            VersionTtl
        );
    }

    public async Task<AccountDto?> GetAccountAsync(Guid userId, Guid accountId)
    {
        var ver = await GetReadModelVersionAsync(userId, accountId);
        var key = AccountKey(userId, accountId, ver);

        var cached = await _accountCache.GetAsync(key);
        if (cached != null)
        {
            _logger.LogInformation(
                "CACHE HIT -> AccountDetail accountId={AccountId} key={CacheKey}",
                accountId,
                key
            );
        }
        else
        {
            _logger.LogInformation(
                "CACHE MISS -> AccountDetail accountId={AccountId} key={CacheKey}",
                accountId,
                key
            );
        }

        return cached;
    }

    public async Task SetAccountAsync(Guid userId, Guid accountId, AccountDto dto)
    {
        var ver = await GetReadModelVersionAsync(userId, accountId);
        var key = AccountKey(userId, accountId, ver);

        await _accountCache.SetAsync(key, dto, AccountTtl);
    }

    public async Task<PagedResultDto<AccountListItemDto>?> GetAccountsListAsync(Guid userId, MyAccountsInput input)
    {
        var ver = await GetAccountsListVersionAsync(userId);
        var key = AccountsListKey(userId, input, ver);

        var cached = await _accountsListCache.GetAsync(key);
        if (cached != null)
        {
            _logger.LogInformation(
                "CACHE HIT -> AccountsList userId={UserId} key={CacheKey}",
                userId,
                key
            );
        }
        else
        {
            _logger.LogInformation(
                "CACHE MISS -> AccountsList userId={UserId} key={CacheKey}",
                userId,
                key
            );
        }

        return cached;
    }

    public async Task SetAccountsListAsync(Guid userId, MyAccountsInput input, PagedResultDto<AccountListItemDto> dto)
    {
        var ver = await GetAccountsListVersionAsync(userId);
        var key = AccountsListKey(userId, input, ver);

        await _accountsListCache.SetAsync(key, dto, AccountsListTtl);
    }

    public async Task<AccountSummaryDto?> GetSummaryAsync(Guid userId, Guid accountId)
    {
        var ver = await GetReadModelVersionAsync(userId, accountId);
        var key = SummaryKey(userId, accountId, ver);

        var cached = await _summaryCache.GetAsync(key);
        if (cached != null)
        {
            _logger.LogInformation(
                "CACHE HIT -> AccountSummary accountId={AccountId} key={CacheKey}",
                accountId,
                key
            );
        }
        else
        {
            _logger.LogInformation(
                "CACHE MISS -> AccountSummary accountId={AccountId} key={CacheKey}",
                accountId,
                key
            );
        }

        return cached;
    }

    public async Task SetSummaryAsync(Guid userId, Guid accountId, AccountSummaryDto dto)
    {
        var ver = await GetReadModelVersionAsync(userId, accountId);
        var key = SummaryKey(userId, accountId, ver);

        await _summaryCache.SetAsync(key, dto, SummaryTtl);
    }

    public async Task<PagedResultDto<TransactionDto>?> GetStatementAsync(Guid userId, GetAccountStatementInput input)
    {
        var ver = await GetReadModelVersionAsync(userId, input.AccountId);
        var key = StatementKey(userId, input, ver);

        var cached = await _statementCache.GetAsync(key);
        if (cached != null)
        {
            _logger.LogInformation(
                "CACHE HIT -> AccountStatement accountId={AccountId} key={CacheKey}",
                input.AccountId,
                key
            );
        }
        else
        {
            _logger.LogInformation(
                "CACHE MISS -> AccountStatement accountId={AccountId} key={CacheKey}",
                input.AccountId,
                key
            );
        }

        return cached;
    }

    public async Task SetStatementAsync(Guid userId, GetAccountStatementInput input, PagedResultDto<TransactionDto> dto)
    {
        var ver = await GetReadModelVersionAsync(userId, input.AccountId);
        var key = StatementKey(userId, input, ver);

        await _statementCache.SetAsync(key, dto, StatementTtl);
    }

    public async Task InvalidateAccountsListAsync(Guid userId)
    {
        await BumpAccountsListVersionAsync(userId);

        _logger.LogInformation(
            "CACHE INVALIDATE -> accounts-list userId={UserId}",
            userId
        );
    }

    public async Task InvalidateAccountReadModelsAsync(Guid userId, Guid accountId)
    {
        await BumpReadModelVersionAsync(userId, accountId);
        await InvalidateAccountsListAsync(userId);

        _logger.LogInformation(
            "CACHE INVALIDATE -> accountId={AccountId} userId={UserId}",
            accountId,
            userId
        );
    }
}