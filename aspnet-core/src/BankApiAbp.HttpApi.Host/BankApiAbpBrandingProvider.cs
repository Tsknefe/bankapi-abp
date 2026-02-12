using Microsoft.Extensions.Localization;
using BankApiAbp.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace BankApiAbp;

[Dependency(ReplaceServices = true)]
public class BankApiAbpBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<BankApiAbpResource> _localizer;

    public BankApiAbpBrandingProvider(IStringLocalizer<BankApiAbpResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
