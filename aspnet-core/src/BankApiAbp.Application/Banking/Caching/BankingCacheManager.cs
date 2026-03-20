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
    private readonly IDistributedCache<string> _versionCache;
    private readonly ILogger<BankingCacheManager> _logger;

    public BankingCacheManager(
        IDistributedCache<string> versionCache,
        ILogger<BankingCacheManager> logger)
    {
        _versionCache = versionCache;
        _logger = logger;
    }

    private static DistributedCacheEntryOptions VersionTtl =>
        new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30)
        };

    private static string AccountVersionKey(Guid userId, Guid accountId)
        => $"acct:ver:u:{userId}:a:{accountId}";

    private static string AccountsListVersionKey(Guid userId)
        => $"acct:listver:u:{userId}";

    public string BuildAccountDetailKey(Guid userId, Guid accountId, int version)
        => $"acct:detail:u:{userId}:a:{accountId}:v:{version}";

    public string BuildAccountSummaryKey(Guid userId, Guid accountId, int version)
        => $"acct:summary:u:{userId}:a:{accountId}:v:{version}";

    public string BuildAccountStatementKey(Guid userId, GetAccountStatementInput input, int version)
        => $"acct:stmt:u:{userId}:a:{input.AccountId}:v:{version}"
           + $":f:{(input.From.HasValue ? input.From.Value.ToString("O", CultureInfo.InvariantCulture) : "null")}"
           + $":t:{(input.To.HasValue ? input.To.Value.ToString("O", CultureInfo.InvariantCulture) : "null")}"
           + $":s:{input.SkipCount}:m:{input.MaxResultCount}";

    public string BuildAccountsListKey(Guid userId, MyAccountsInput input, int version)
        => $"acct:list:u:{userId}:v:{version}"
           + $":c:{(input.CustomerId.HasValue ? input.CustomerId.Value.ToString() : "null")}"
           + $":f:{(string.IsNullOrWhiteSpace(input.Filter) ? "null" : input.Filter.Trim())}"
           + $":s:{input.SkipCount}:m:{input.MaxResultCount}";

    public async Task<int> GetAccountReadModelVersionAsync(Guid userId, Guid accountId)
    {
        var v = await _versionCache.GetAsync(AccountVersionKey(userId, accountId));
        if (string.IsNullOrWhiteSpace(v)) return 1;
        return int.TryParse(v, out var parsed) ? parsed : 1;
    }

    public async Task<int> GetAccountsListVersionAsync(Guid userId)
    {
        var v = await _versionCache.GetAsync(AccountsListVersionKey(userId));
        if (string.IsNullOrWhiteSpace(v)) return 1;
        return int.TryParse(v, out var parsed) ? parsed : 1;
    }

    public async Task InvalidateAccountReadModelsAsync(Guid userId, Guid accountId)
    {
        var current = await GetAccountReadModelVersionAsync(userId, accountId);
        var next = current + 1;

        await _versionCache.SetAsync(
            AccountVersionKey(userId, accountId),
            next.ToString(CultureInfo.InvariantCulture),
            VersionTtl);

        await InvalidateAccountsListAsync(userId);

        _logger.LogInformation(
            "CACHE INVALIDATE -> accountId={AccountId} userId={UserId} nextVersion={Version}",
            accountId,
            userId,
            next);
    }

    public async Task InvalidateAccountsListAsync(Guid userId)
    {
        var current = await GetAccountsListVersionAsync(userId);
        var next = current + 1;

        await _versionCache.SetAsync(
            AccountsListVersionKey(userId),
            next.ToString(CultureInfo.InvariantCulture),
            VersionTtl);

        _logger.LogInformation(
            "CACHE INVALIDATE -> accounts-list userId={UserId} nextVersion={Version}",
            userId,
            next);
    }
}