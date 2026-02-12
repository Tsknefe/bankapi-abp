using BankApiAbp.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace BankApiAbp.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(BankApiAbpEntityFrameworkCoreModule),
    typeof(BankApiAbpApplicationContractsModule)

    )]
public class BankApiAbpDbMigratorModule : AbpModule
{
}
