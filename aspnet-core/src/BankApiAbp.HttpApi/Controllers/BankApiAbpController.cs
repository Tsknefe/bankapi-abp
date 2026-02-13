using BankApiAbp.Localization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace BankApiAbp.Controllers;

/* Inherit your controllers from this class.
 */
[IgnoreAntiforgeryToken]
public abstract class BankApiAbpController : AbpControllerBase
{
    protected BankApiAbpController()
    {
        LocalizationResource = typeof(BankApiAbpResource);
    }
}
