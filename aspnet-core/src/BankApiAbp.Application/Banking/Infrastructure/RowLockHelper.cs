using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BankApiAbp.EntityFrameworkCore;
using BankApiAbp.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;

namespace BankApiAbp.Banking.Infrastructure;


public class RowLockHelper : ITransientDependency
{
    private readonly IDbContextProvider<BankApiAbpDbContext> _dbContextProvider;

    public RowLockHelper(IDbContextProvider<BankApiAbpDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<Account> LockAccountForUpdateAsync(Guid accountId, CancellationToken ct = default)
    {
        var db = await _dbContextProvider.GetDbContextAsync();

        var account = await db.Accounts
            .FromSqlInterpolated($@"SELECT * FROM ""Accounts"" WHERE ""Id"" = {accountId} FOR UPDATE")
            .AsTracking()
            .SingleOrDefaultAsync(ct);

        if (account == null)
            throw new EntityNotFoundException(typeof(Account), accountId);

        return account;
    }

    public async Task<(Account First, Account Second)> LockTwoAccountsForUpdateAsync(
        Guid accountId1,
        Guid accountId2,
        CancellationToken ct = default)
    {
        if (accountId1 == Guid.Empty || accountId2 == Guid.Empty)
            throw new BusinessException("ACCOUNT_ID_INVALID");

        if (accountId1 == accountId2)
        {
            var one = await LockAccountForUpdateAsync(accountId1, ct);
            return (one, one);
        }

        var firstId = accountId1.CompareTo(accountId2) < 0 ? accountId1 : accountId2;
        var secondId = firstId == accountId1 ? accountId2 : accountId1;

        var first = await LockAccountForUpdateAsync(firstId, ct);
        var second = await LockAccountForUpdateAsync(secondId, ct);

        return (first, second);
    }

    public async Task<Account[]> LockAccountsForUpdateAsync(Guid[] accountIds, CancellationToken ct = default)
    {
        if (accountIds == null || accountIds.Length == 0)
            return Array.Empty<Account>();

        var distinctSorted = accountIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var locked = new Account[distinctSorted.Length];
        for (int i = 0; i < distinctSorted.Length; i++)
        {
            locked[i] = await LockAccountForUpdateAsync(distinctSorted[i], ct);
        }

        return locked;
    }
}