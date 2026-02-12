using Volo.Abp.Modularity;

namespace BankApiAbp;

[DependsOn(
    typeof(BankApiAbpDomainModule),
    typeof(BankApiAbpTestBaseModule)
)]
public class BankApiAbpDomainTestModule : AbpModule
{

}
