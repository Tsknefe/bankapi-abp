using System.Diagnostics;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    private static readonly ActivitySource ActivitySource = new("BankApiAbp.Banking");
}