using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Account;
using Volo.Abp.AutoMapper;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using BankApiAbp.Banking.Risk;
namespace BankApiAbp;

[DependsOn(
    typeof(BankApiAbpDomainModule),
    typeof(AbpAccountApplicationModule),
    typeof(BankApiAbpApplicationContractsModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule)
)]
public class BankApiAbpApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHttpContextAccessor();

        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<BankApiAbpApplicationModule>();
        });

        context.Services.AddTransient<IRiskRule, AmountRiskRule>();
        context.Services.AddTransient<IRiskRule, NightRiskRule>();
        context.Services.AddTransient<IRiskRule, VelocityRiskRule>();

        context.Services.AddTransient<ITransactionRiskEngine, TransactionRiskEngine>();
    }
}