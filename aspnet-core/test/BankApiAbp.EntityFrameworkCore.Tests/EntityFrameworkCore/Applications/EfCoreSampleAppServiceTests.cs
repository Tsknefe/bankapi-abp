using BankApiAbp.Samples;
using Xunit;

namespace BankApiAbp.EntityFrameworkCore.Applications;

[Collection(BankApiAbpTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<BankApiAbpEntityFrameworkCoreTestModule>
{

}
