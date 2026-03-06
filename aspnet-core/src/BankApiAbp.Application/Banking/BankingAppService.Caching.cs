using System;
using System.Globalization;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Caching;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    private IDistributedCache<AccountSummaryDto> SummaryCache =>
        LazyServiceProvider.LazyGetRequiredService<IDistributedCache<AccountSummaryDto>>();

    private IDistributedCache<PagedResultDto<TransactionDto>> StatementCache =>
        LazyServiceProvider.LazyGetRequiredService<IDistributedCache<PagedResultDto<TransactionDto>>>();

    private IDistributedCache<string> VersionCache =>
        LazyServiceProvider.LazyGetRequiredService<IDistributedCache<string>>();

    private static DistributedCacheEntryOptions SummaryTtl =>
        new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
        };

    private static DistributedCacheEntryOptions StatementTtl =>
        new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15)
        };

    private string SummaryKey(Guid userId, Guid accountId, int version)
        => $"acct:summary:u:{userId}:a:{accountId}:v:{version}";

    private string StatementKey(Guid userId, GetAccountStatementInput input, int version)
        => $"acct:stmt:u:{userId}:a:{input.AccountId}:v:{version}"
           + $":f:{(input.From.HasValue ? input.From.Value.ToString("O", CultureInfo.InvariantCulture) : "null")}"
           + $":t:{(input.To.HasValue ? input.To.Value.ToString("O", CultureInfo.InvariantCulture) : "null")}"
           + $":s:{input.SkipCount}:m:{input.MaxResultCount}";

    private string VersionKey(Guid userId, Guid accountId)
        => $"acct:ver:u:{userId}:a:{accountId}";

    protected async Task<int> GetReadModelVersionAsync(Guid userId, Guid accountId)
    {
        var v = await VersionCache.GetAsync(VersionKey(userId, accountId));
        if (string.IsNullOrWhiteSpace(v)) return 1;
        return int.TryParse(v, out var parsed) ? parsed : 1;
    }

    private async Task BumpReadModelVersionAsync(Guid userId, Guid accountId)
    {
        var current = await GetReadModelVersionAsync(userId, accountId);
        var next = current + 1;

        await VersionCache.SetAsync(
            VersionKey(userId, accountId),
            next.ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30)
            }
        );
    }

    protected async Task InvalidateAccountReadModelsAsync(Guid userId, Guid accountId)
    {
        await BumpReadModelVersionAsync(userId, accountId);
    }
}