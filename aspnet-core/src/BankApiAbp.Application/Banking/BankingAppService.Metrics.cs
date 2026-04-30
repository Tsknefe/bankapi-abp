using System.Diagnostics.Metrics;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    public const string MeterName = "BankApiAbp.Banking";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> AccountSummaryCacheHitCounter =
        Meter.CreateCounter<long>("banking.account_summary.cache.hit");

    private static readonly Counter<long> AccountSummaryCacheMissCounter =
        Meter.CreateCounter<long>("banking.account_summary.cache.miss");

    private static readonly Counter<long> AccountStatementCacheHitCounter =
        Meter.CreateCounter<long>("banking.account_statement.cache.hit");

    private static readonly Counter<long> AccountStatementCacheMissCounter =
        Meter.CreateCounter<long>("banking.account_statement.cache.miss");

    private static readonly Counter<long> DepositRequestCounter =
        Meter.CreateCounter<long>("banking.deposit.request");

    private static readonly Counter<long> DepositSuccessCounter =
        Meter.CreateCounter<long>("banking.deposit.success");

    private static readonly Counter<long> DepositFailureCounter =
        Meter.CreateCounter<long>("banking.deposit.failure");

    private static readonly Histogram<double> DepositDurationMs =
        Meter.CreateHistogram<double>("banking.deposit.duration.ms");

    private static readonly Counter<long> WithdrawRequestCounter =
        Meter.CreateCounter<long>("banking.withdraw.request");

    private static readonly Counter<long> WithdrawSuccessCounter =
        Meter.CreateCounter<long>("banking.withdraw.success");

    private static readonly Counter<long> WithdrawFailureCounter =
        Meter.CreateCounter<long>("banking.withdraw.failure");

    private static readonly Histogram<double> WithdrawDurationMs =
        Meter.CreateHistogram<double>("banking.withdraw.duration.ms");

    private static readonly Counter<long> TransferRequestCounter =
        Meter.CreateCounter<long>("banking.transfer.request");

    private static readonly Counter<long> TransferSuccessCounter =
        Meter.CreateCounter<long>("banking.transfer.success");

    private static readonly Counter<long> TransferFailureCounter =
        Meter.CreateCounter<long>("banking.transfer.failure");

    private static readonly Counter<long> TransferPublishedEventCounter =
        Meter.CreateCounter<long>("banking.transfer.event.published");

    private static readonly Counter<long> TransferLockFailureCounter =
        Meter.CreateCounter<long>("banking.transfer.lock.failure");

    private static readonly Histogram<double> TransferDurationMs =
        Meter.CreateHistogram<double>("banking.transfer.duration.ms");

        public static readonly Counter<long> RiskEvaluationCounter =
        Meter.CreateCounter<long>("banking.risk.evaluations");

        public static readonly Counter<long> RiskAllowedCounter =
            Meter.CreateCounter<long>("banking.risk.allowed");

        public static readonly Counter<long> RiskFlaggedCounter =
            Meter.CreateCounter<long>("banking.risk.flagged");

        public static readonly Counter<long> RiskBlockedCounter =
            Meter.CreateCounter<long>("banking.risk.blocked");

        public static readonly Histogram<int> RiskScoreHistogram =
            Meter.CreateHistogram<int>("banking.risk.score");
}