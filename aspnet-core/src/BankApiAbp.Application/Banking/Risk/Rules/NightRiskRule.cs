using System.Threading.Tasks;

public class NightRiskRule : IRiskRule
{
    public Task<int> EvaluateAsync(RiskContext context)
    {
        if (context.Now.Hour >= 0 && context.Now.Hour <= 5)
            return Task.FromResult(20);

        return Task.FromResult(0);
    }
}