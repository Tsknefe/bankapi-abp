using BankApiAbp.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace BankApiAbp.Permissions;

public class BankApiAbpPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(BankApiAbpPermissions.GroupName);
        //Define your own permissions here. Example:
        //myGroup.AddPermission(BankApiAbpPermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<BankApiAbpResource>(name);
    }
}
