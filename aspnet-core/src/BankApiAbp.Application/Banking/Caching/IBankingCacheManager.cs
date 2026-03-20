using System;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Caching;

public interface IBankingCacheManager
{
    Task<AccountDto?> GetAccountAsync(Guid userId, Guid accountId);
    Task SetAccountAsync(Guid userId, Guid accountId, AccountDto dto);

    Task<PagedResultDto<AccountListItemDto>?> GetAccountsListAsync(Guid userId, MyAccountsInput input);
    Task SetAccountsListAsync(Guid userId, MyAccountsInput input, PagedResultDto<AccountListItemDto> dto);

    Task<AccountSummaryDto?> GetSummaryAsync(Guid userId, Guid accountId);
    Task SetSummaryAsync(Guid userId, Guid accountId, AccountSummaryDto dto);

    Task<PagedResultDto<TransactionDto>?> GetStatementAsync(Guid userId, GetAccountStatementInput input);
    Task SetStatementAsync(Guid userId, GetAccountStatementInput input, PagedResultDto<TransactionDto> dto);

    Task<int> GetReadModelVersionAsync(Guid userId, Guid accountId);
    Task InvalidateAccountReadModelsAsync(Guid userId, Guid accountId);
    Task InvalidateAccountsListAsync(Guid userId);
}