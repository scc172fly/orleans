using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using TestExtensions;
using Orleans.Streaming.EventHubs;
using Tester;
using Microsoft.Extensions.DependencyInjection;
using Tester.StreamingTests;
using Xunit;

namespace ServiceBus.Tests.StreamingTests
{
    /// <summary>
    /// 测试 EventHub 使用自定义流过滤器和缓存驱逐来流缓存误差情形。
    /// </summary>
    [TestCategory("EventHub"), TestCategory("Streaming"), TestCategory("Functional"), TestCategory("StreamingCacheMiss")]
    [TestSuite("Functional")]
    [TestProvider("EventHub")]
    [TestArea("Streaming")]
    public class EHStreamCacheMissTests : StreamingCacheMissTests
    {
        private const string EHPath = "ehorleanstest";
        private const string EHConsumerGroup = "orleansnightly";

        public EHStreamCacheMissTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            TestUtils.CheckForEventHub();
            builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
            builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
        }

        #region Configuration stuff

        private class MySiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder
                    .AddMemoryGrainStorageAsDefault()
                    .AddMemoryGrainStorage("PubSubStore")
                    .AddEventHubStreams(StreamProviderName, b =>
                    {
                        b.ConfigureCacheEviction(ob => ob.Configure(options =>
                        {
                            options.DataMaxAgeInCache = TimeSpan.FromSeconds(5);
                            options.DataMinTimeInCache = TimeSpan.FromSeconds(0);
                            options.MetadataMinTimeInCache = TimeSpan.FromMinutes(1);
                        }));
                        b.ConfigureEventHub(ob => ob.Configure(options =>
                        {
                            options.ConfigureTestDefaults(EHPath, EHConsumerGroup);
                        }));
                        b.UseAzureTableCheckpointer(ob => ob.Configure(options =>
                        {
                            options.ConfigureTestDefaults();
                            options.PersistInterval = TimeSpan.FromSeconds(10);
                        }));
                        b.UseDataAdapter((sp, n) => ActivatorUtilities.CreateInstance<EventHubDataAdapter>(sp));
                    })
                    .AddStreamFilter<CustomStreamFilter>(StreamProviderName);
            }
        }

        private class MyClientBuilderConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder
                    .AddEventHubStreams(StreamProviderName, b =>
                    {
                        b.ConfigureEventHub(ob => ob.Configure(options =>
                        {
                            options.ConfigureTestDefaults(EHPath, EHConsumerGroup);
                        }));
                        b.UseDataAdapter((sp, n) => ActivatorUtilities.CreateInstance<EventHubDataAdapter>(sp));
                    });
            }
        }

        #endregion
    }
}
