using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Transactions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
public class VelocityRiskRule : IRiskRule
{
    private readonly IRepository<Transaction, Guid> _tx;

    public VelocityRiskRule(IRepository<Transaction, Guid> tx)
    {
        _tx = tx;
    }

    public async Task<int> EvaluateAsync(RiskContext context)
    {
        var lastMinute = DateTime.UtcNow.AddMinutes(-1);

        var q = await _tx.GetQueryableAsync();

        var count = q.Count(t =>
            t.AccountId == context.FromAccountId &&
            t.CreationTime >= lastMinute);

        if (count >= 3)
            return 40;

        return 0;
    }
}