using BankApiAbp.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;

namespace BankApiAbp.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(BankApiAbpEntityFrameworkCoreModule),
    typeof(BankApiAbpApplicationContractsModule)
)]
public class BankApiAbpDbMigratorModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpDistributedEventBusOptions>(options =>
        {
            options.Outboxes.Configure(config =>
            {
                config.IsSendingEnabled = false;
            });

            options.Inboxes.Configure(config =>
            {
                config.IsProcessingEnabled = false;
            });
        });
    }
}