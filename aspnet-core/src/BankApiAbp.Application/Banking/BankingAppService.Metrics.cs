using System.Diagnostics.Metrics;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    private static readonly Meter Meter = new("BankApiAbp.Banking");

    private static readonly Counter<long> AccountSummaryCacheHitCounter =
        Meter.CreateCounter<long>("banking.account_summary.cache.hit");

    private static readonly Counter<long> AccountSummaryCacheMissCounter =
        Meter.CreateCounter<long>("banking.account_summary.cache.miss");

    private static readonly Counter<long> AccountStatementCacheHitCounter =
        Meter.CreateCounter<long>("banking.account_statement.cache.hit");

    private static readonly Counter<long> AccountStatementCacheMissCounter =
        Meter.CreateCounter<long>("banking.account_statement.cache.miss");
}