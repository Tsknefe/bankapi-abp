using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Transactions;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace BankApiAbp.Banking.Risk;

public class TransactionRiskEngine :
    ITransactionRiskEngine,
    ITransientDependency
{
    private const decimal DailyTransferLimit = 1_000_000m;
    private const decimal HighAmountThreshold = 50_000m;
    private const int VelocityWindowSeconds = 10;
    private const int VelocityMaxTransferCount = 3;

    private const int FlagScoreThreshold = 40;
    private const int BlockScoreThreshold = 80;

    private readonly IRepository<Transaction, Guid> _transactions;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ILogger<TransactionRiskEngine> _logger;

    public TransactionRiskEngine(
        IRepository<Transaction, Guid> transactions,
        IAsyncQueryableExecuter asyncExecuter,
        ILogger<TransactionRiskEngine> logger)
    {
        _transactions = transactions;
        _asyncExecuter = asyncExecuter;
        _logger = logger;
    }

    public async Task<RiskEvaluationResult> EvaluateTransferAsync(Guid userId, TransferDto input)
    {
        var score = 0;

        if (input.FromAccountId == input.ToAccountId)
        {
            return RiskEvaluationResult.Block(
                "RISK:SELF_TRANSFER_BLOCKED",
                "Aynı hesaba transfer yapılamaz.",
                100);
        }

        if (input.Amount <= 0)
        {
            return RiskEvaluationResult.Block(
                "RISK:INVALID_AMOUNT",
                "Transfer tutarı sıfırdan büyük olmalıdır.",
                100);
        }

        if (input.Amount > DailyTransferLimit)
        {
            return RiskEvaluationResult.Block(
                "RISK:SINGLE_TRANSFER_LIMIT_EXCEEDED",
                "Tek işlem tutarı günlük limitten büyük olamaz.",
                100);
        }

        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var velocityStart = now.AddSeconds(-VelocityWindowSeconds);

        var query = await _transactions.GetQueryableAsync();

        var outgoingTransfers = query.Where(x =>
            x.AccountId == input.FromAccountId &&
            x.TxType == TransactionType.TransferOut);

        var todayTotal =
            await _asyncExecuter.SumAsync(
                outgoingTransfers.Where(x => x.CreationTime >= todayStart),
                x => (decimal?)x.Amount) ?? 0m;

        if (todayTotal + input.Amount > DailyTransferLimit)
        {
            return RiskEvaluationResult.Block(
                "RISK:DAILY_TRANSFER_LIMIT_EXCEEDED",
                "Günlük transfer limiti aşıldı.",
                100);
        }

        var recentTransferCount =
            await _asyncExecuter.CountAsync(
                outgoingTransfers.Where(x => x.CreationTime >= velocityStart));

        if (recentTransferCount >= VelocityMaxTransferCount)
        {
            score += 60;
        }

        if (input.Amount >= HighAmountThreshold)
        {
            score += 50;
        }

        if (now.Hour is >= 0 and <= 5)
        {
            score += 10;
        }

        if (score >= BlockScoreThreshold)
        {
            _logger.LogWarning(
                "Transfer blocked by risk score. UserId={UserId}, FromAccountId={FromAccountId}, ToAccountId={ToAccountId}, Amount={Amount}, RiskScore={RiskScore}",
                userId,
                input.FromAccountId,
                input.ToAccountId,
                input.Amount,
                score);

            return RiskEvaluationResult.Block(
                "RISK:SCORE_BLOCKED",
                "Risk skoru çok yüksek olduğu için işlem engellendi.",
                score);
        }

        if (score >= FlagScoreThreshold)
        {
            _logger.LogWarning(
                "Transfer flagged by risk score. UserId={UserId}, FromAccountId={FromAccountId}, ToAccountId={ToAccountId}, Amount={Amount}, RiskScore={RiskScore}",
                userId,
                input.FromAccountId,
                input.ToAccountId,
                input.Amount,
                score);

            return RiskEvaluationResult.Flag(
                "RISK:SCORE_FLAGGED",
                "İşlem riskli olarak işaretlendi.",
                score);
        }

        return RiskEvaluationResult.Allow(score);
    }
}