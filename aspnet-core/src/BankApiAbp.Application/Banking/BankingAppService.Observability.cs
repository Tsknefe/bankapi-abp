using System.Diagnostics;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    public const string ActivitySourceName = "BankingTracing";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}