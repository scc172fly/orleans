using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using TestExtensions;
using TestExtensions.Runners;
using Xunit;

namespace Tester.AzureUtils.Persistence
{
    /// <summary>
    /// Base_PersistenceGrainTests：用于测试持久化提供程序的基类。
    /// </summary>
    public abstract class Base_PersistenceGrainTests_AzureStore : GrainPersistenceTestsRunner
    {
        public class SiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.UseAzureStorageClustering(options =>
                {
                    options.ConfigureTestDefaults();
                });
            }
        }

        public class ClientBuilderConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder.UseAzureStorageClustering(gatewayOptions => { gatewayOptions.ConfigureTestDefaults(); });
            }
        }

        protected Base_PersistenceGrainTests_AzureStore(ITestOutputHelper output, BaseTestClusterFixture fixture, string grainNamespace = "UnitTests.Grains")
            : base(output, fixture, grainNamespace)
        {
        }
    }
}
