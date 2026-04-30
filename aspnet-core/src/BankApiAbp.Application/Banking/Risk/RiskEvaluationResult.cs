namespace BankApiAbp.Banking.Risk;

public class RiskEvaluationResult
{
    public RiskDecision Decision { get; private set; }
    public string Code { get; private set; }
    public string Message { get; private set; }
    public int Score { get; private set; }

    private RiskEvaluationResult(
        RiskDecision decision,
        string code,
        string message,
        int score)
    {
        Decision = decision;
        Code = code;
        Message = message;
        Score = score;
    }

    public static RiskEvaluationResult Allow(int score = 0)
        => new(RiskDecision.Allow, "RISK:ALLOW", "Transaction allowed.", score);

    public static RiskEvaluationResult Flag(string code, string message, int score)
        => new(RiskDecision.Flag, code, message, score);

    public static RiskEvaluationResult Block(string code, string message, int score)
        => new(RiskDecision.Block, code, message, score);
}