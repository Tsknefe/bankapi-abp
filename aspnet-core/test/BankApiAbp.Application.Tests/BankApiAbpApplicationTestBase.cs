using Volo.Abp.Modularity;

namespace BankApiAbp;

public abstract class BankApiAbpApplicationTestBase<TStartupModule> : BankApiAbpTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
