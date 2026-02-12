using BankApiAbp.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace BankApiAbp.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class BankApiAbpController : AbpControllerBase
{
    protected BankApiAbpController()
    {
        LocalizationResource = typeof(BankApiAbpResource);
    }
}
