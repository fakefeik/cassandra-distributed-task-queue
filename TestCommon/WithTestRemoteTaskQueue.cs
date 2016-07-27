using System.Reflection;

using SKBKontur.Catalogue.NUnit.Extensions.CommonWrappers;
using SKBKontur.Catalogue.NUnit.Extensions.EdiTestMachinery;
using SKBKontur.Catalogue.NUnit.Extensions.EdiTestMachinery.Impl.TestContext;

namespace TestCommon
{
    [WithCassandra("CatalogueCluster", "QueueKeyspace")]
    public class WithTestRemoteTaskQueue : EdiTestSuiteWrapperAttribute
    {
        public override sealed void SetUp(string suiteName, Assembly testAssembly, IEditableEdiTestContext suiteContext)
        {
            suiteContext.Container.ConfigureRemoteTaskQueue();
        }
    }
}