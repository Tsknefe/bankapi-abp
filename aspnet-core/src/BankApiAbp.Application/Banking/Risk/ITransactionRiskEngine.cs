using System;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;

namespace BankApiAbp.Banking.Risk;

public interface ITransactionRiskEngine
{
    Task<RiskEvaluationResult> EvaluateTransferAsync(Guid userId, TransferDto input);
}