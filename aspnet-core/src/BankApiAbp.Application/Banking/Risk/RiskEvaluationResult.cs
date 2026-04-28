namespace BankApiAbp.Banking.Risk;

public class RiskEvaluationResult
{
    public RiskDecision Decision { get; private set; }
    public string Code { get; private set; }
    public string Message { get; private set; }

    private RiskEvaluationResult(RiskDecision decision, string code, string message)
    {
        Decision = decision;
        Code = code;
        Message = message;
    }

    public static RiskEvaluationResult Allow()
        => new(RiskDecision.Allow, "RISK:ALLOW", "Transaction allowed.");

    public static RiskEvaluationResult Flag(string code, string message)
        => new(RiskDecision.Flag, code, message);

    public static RiskEvaluationResult Block(string code, string message)
        => new(RiskDecision.Block, code, message);
}