using System.Threading.Tasks;

public interface IRiskRule
{
    Task<int> EvaluateAsync(RiskContext context);
}