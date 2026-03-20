using System;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Caching;

public interface IBankingCacheManager
{
    Task<int> GetAccountReadModelVersionAsync(Guid userId, Guid accountId);
    Task<int> GetAccountsListVersionAsync(Guid userId);

    string BuildAccountDetailKey(Guid userId, Guid accountId, int version);
    string BuildAccountSummaryKey(Guid userId, Guid accountId, int version);
    string BuildAccountStatementKey(Guid userId, GetAccountStatementInput input, int version);
    string BuildAccountsListKey(Guid userId, MyAccountsInput input, int version);

    Task InvalidateAccountReadModelsAsync(Guid userId, Guid accountId);
    Task InvalidateAccountsListAsync(Guid userId);
}