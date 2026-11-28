using Orleans.TestingHost;
using TestExtensions;
using Tester.AzureUtils;
using UnitTests.Grains.Directories;

namespace Tester.Directories
{
    /// <summary>
    /// 测试以 Azure Table Storage 为目录后端的自定义 Grain 目录功能。
    /// </summary>
    [TestCategory("AzureStorage")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Membership")]
    public class AzureMultipleGrainDirectoriesTests : MultipleGrainDirectoriesTests
    {
        public class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder.AddAzureTableGrainDirectory(
                    CustomDirectoryGrain.DIRECTORY,
                    options => options.TableServiceClient = AzureStorageOperationOptionsExtensions.GetTableServiceClient());
            }
        }

        protected override void CheckPreconditionsOrThrow() => TestUtils.CheckForAzureStorage();

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            EnsurePreconditionsMet();

            base.ConfigureTestCluster(builder);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        }
    }
}
