using System.Threading.Tasks;

public class AmountRiskRule : IRiskRule
{
    public Task<int> EvaluateAsync(RiskContext context)
    {
        if (context.Amount >= 50000)
            return Task.FromResult(50);

        if (context.Amount >= 10000)
            return Task.FromResult(20);

        return Task.FromResult(0);
    }
}