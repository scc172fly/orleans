using Orleans.TestingHost;
using TestExtensions;
using Tester.AzureUtils;
using UnitTests.Grains.Directories;

namespace Tester.Directories
{
    /// <summary>
    /// 自定义测试 grain 使用 Azure 表格存储为目录后端 。
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
