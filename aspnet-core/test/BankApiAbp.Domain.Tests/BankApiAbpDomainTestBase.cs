using Volo.Abp.Modularity;

namespace BankApiAbp;

/* Inherit from this class for your domain layer tests. */
public abstract class BankApiAbpDomainTestBase<TStartupModule> : BankApiAbpTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
