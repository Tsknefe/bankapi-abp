using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.PermissionManagement;

namespace BankApiAbp;

[DependsOn(
    typeof(BankApiAbpApplicationModule),
    typeof(BankApiAbpDomainTestModule),
    typeof(AbpPermissionManagementDomainModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule)
)]
public class BankApiAbpApplicationTestModule : AbpModule
{
}