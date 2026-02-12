using Volo.Abp.Settings;

namespace BankApiAbp.Settings;

public class BankApiAbpSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(BankApiAbpSettings.MySetting1));
    }
}
