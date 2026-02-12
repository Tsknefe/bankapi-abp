using System;
using System.Collections.Generic;
using System.Text;
using BankApiAbp.Localization;
using Volo.Abp.Application.Services;

namespace BankApiAbp;

/* Inherit your application services from this class.
 */
public abstract class BankApiAbpAppService : ApplicationService
{
    protected BankApiAbpAppService()
    {
        LocalizationResource = typeof(BankApiAbpResource);
    }
}
