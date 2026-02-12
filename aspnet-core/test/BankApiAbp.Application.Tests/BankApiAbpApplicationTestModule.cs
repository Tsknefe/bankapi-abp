using Volo.Abp.Modularity;

namespace BankApiAbp;

[DependsOn(
    typeof(BankApiAbpApplicationModule),
    typeof(BankApiAbpDomainTestModule)
)]
public class BankApiAbpApplicationTestModule : AbpModule
{

}
